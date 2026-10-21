<#
.SYNOPSIS
    Scans every main screen with the Accessibility Insights rules (Axe.Windows, tools/TenLapse.A11yScan) and walks
    the Tab order on each (#38).

.DESCRIPTION
    Runs the Debug app five times with --focus-walk and drives it through UI Automation patterns on its own window and
    Debug launch switches only (no synthetic input, no Windows setting changed). On each screen it runs the scan, which
    writes <screen>.txt (one line per failure) and, for a screen with failures, an .a11ytest file that opens in
    Accessibility Insights, into %LOCALAPPDATA%\Temp\claude\<Branch>\a11y\<Label>:

        first-launch-infobar  first launch with the missing-extension InfoBar and its Store button (--missing-codec)
        first-launch          the same, the bar closed
        settings              the Settings page
        editing-infobar       the skipped-photos InfoBar of an import of the broken test photos, its list open
        editing-wide          editing at 1280 x 800
        narrow-timing, narrow-frame, narrow-output   900 px wide, each tab of the panel
        export-running        the export panel at 1280 x 800
        close-question        the close-during-export dialog (skipped when the export finished first)
        finished, finished-narrow   the finished state
        new-project-question  New project's question on a project with changes (the shortcuts run)

    The fourth run (shortcuts) proves the keyboard-only path without pressing a key: Debug --shortcut-after runs Ctrl+I,
    Ctrl+N, Ctrl+E, Ctrl+N, Ctrl+E and Ctrl+I at fixed seconds through the method each keyboard accelerator calls, on
    Editing (with "24" typed into the Timing box and not committed), the question, the finished state, the Empty screen
    and the Settings page; focus-keys.log says which applied. The fifth run (newproject) confirms New project with an
    InfoBar open; focus-newproject.log shows where focus landed.
    Each scan also lists the screen's accelerator keys (UIA AcceleratorKey) in summary.txt. -Only empty,editing,export,keys
    runs just those runs (also newproject).

    With --focus-walk the app writes focus.log into each run's log folder: after each screen change it records where
    focus landed, walks the Tab order (FocusManager.TryMoveFocus, in the app) with each stop's name, and puts focus back.
    The script copies those logs next to the scans. Build the app and the scan tool Debug x64 first (dotnet build
    TenLapse.slnx -c Debug -p:Platform=x64). -Clean deletes everything but the results.

.EXAMPLE
    pwsh -File scripts/a11y-check.ps1 -Branch feat-38-accessibility -Label after
#>
param(
    [string]$Branch = 'feat-38-accessibility',
    [string]$Label = 'scan',
    [int]$ExportPhotos = 400,
    [string[]]$Only = @(),
    [switch]$Clean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'src/TenLapse.App/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64/TenLapse.App.exe'
$scanner = Join-Path $root 'tools/TenLapse.A11yScan/bin/x64/Debug/net10.0-windows10.0.26100.0/TenLapse.A11yScan.exe'
foreach ($file in $exe, $scanner) { if (-not (Test-Path $file)) { throw "Build Debug x64 first: $file" } }

$base = Join-Path $env:LOCALAPPDATA "Temp\claude\$Branch"
$results = Join-Path $base "a11y\$Label"
$samples = Join-Path $base 'samples'
$broken = Join-Path $base 'broken-photos'
Remove-Item $results -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $base, $results, $samples, $broken | Out-Null
foreach ($name in 'empty', 'editing', 'export', 'keys', 'newproject') {
    # Fresh settings too: the app keeps the last rate, which would make the next run's typed 24 the stored value.
    Remove-Item (Join-Path $base "a11y-logs-$name"), (Join-Path $base "a11y-settings-$name") -Recurse -Force -ErrorAction SilentlyContinue
}
Copy-Item (Join-Path $root 'tests/TenLapse.Tests/Fixtures/Photos/*') $broken -Force

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$Tree = [System.Windows.Automation.TreeScope]

function Common([string]$name) {
    @(
        # The checks find controls by their English UIA names, so the app speaks English whatever Windows says (#113).
        '--lang=en-US',
        '--focus-walk',
        "--settings-folder=$(Join-Path $base "a11y-settings-$name")",
        "--cache-folder=$(Join-Path $base "a11y-cache-$name")",
        "--log-folder=$(Join-Path $base "a11y-logs-$name")",
        "--encoder-cache=$(Join-Path $base 'encoder-cache.json')",
        "--sample-folder=$samples",
        '--sample-clean'
    )
}

function Start-App([string[]]$appArgs) {
    $quoted = $appArgs | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    $p = Start-Process -FilePath $exe -ArgumentList $quoted -PassThru
    $window = $null
    $until = (Get-Date).AddSeconds(30)
    while (-not $window -and (Get-Date) -lt $until) {
        Start-Sleep -Milliseconds 300
        $condition = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $p.Id)
        $window = $AE::RootElement.FindFirst($Tree::Children, $condition)
    }
    if (-not $window) { Stop-Process -Id $p.Id -Force; throw 'The window did not appear.' }
    @{ Process = $p; Window = $window }
}

function Stop-App($app) {
    if (-not $app.Process.HasExited) { Stop-Process -Id $app.Process.Id -Force; $app.Process.WaitForExit() }
}

# -Only empty,editing,export,keys runs just those runs (also newproject).
$Only = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
function Want([string]$name) { $Only.Count -eq 0 -or $Only -contains $name }

$skipped = [System.Collections.Generic.List[string]]::new()
$summary = [System.Collections.Generic.List[string]]::new()

function Skip([string]$name, [string]$why) {
    $skipped.Add("$name ($why)")
    Write-Host "  skipped $name`: $why"
}

$notScanned = [System.Collections.Generic.List[string]]::new()

# Runs the scan tool on the app's window. Exit 0: the scan ran, and its line is "<name>: N failures". Any other exit: it
# could not run (an element went away under it, say), so it tries once more, then records the screen as not scanned and
# the script fails at the end. Never records a stack trace as a result.
function Invoke-Scan($app, [string]$name) {
    $line = ''
    for ($try = 1; $try -le 2; $try++) {
        if ($app.Process.HasExited) { Skip $name 'the app has exited'; return }
        $output = @(& $scanner $app.Process.Id $results $name 2>&1)
        $code = $LASTEXITCODE
        $line = "$($output | Select-Object -Last 1)"
        if ($code -eq 0) {
            $summary.Add($line)
            Write-Host "  $line"
            return
        }
        Write-Host "  scan $try of $name could not run (exit $code): $line"
        Start-Sleep -Milliseconds 1000
    }
    $notScanned.Add($name)
    $summary.Add("$name`: NOT SCANNED ($line)")
}

# Waits for the screen to settle (motion, the focus walk), then scans it.
function Scan($app, [string]$name) {
    Start-Sleep -Milliseconds 2500
    if ($app.Process.HasExited) { Skip $name 'the app has exited'; return }
    Invoke-Scan $app $name
    # The accelerator keys a screen reader hears on this screen (UIA AcceleratorKey), on screen only.
    $hasKey = New-Object System.Windows.Automation.NotCondition((New-Object System.Windows.Automation.PropertyCondition($AE::AcceleratorKeyProperty, '')))
    $keys = @($app.Window.FindAll($Tree::Descendants, $hasKey)) | Where-Object { -not $_.Current.IsOffscreen } |
        ForEach-Object { "$($_.Current.Name)=$($_.Current.AcceleratorKey)" }
    $summary.Add("  keys on $name`: $($keys -join '; ')")
}

function Find-Id($app, [string]$id, [double]$seconds = 10) {
    $until = (Get-Date).AddSeconds($seconds)
    do {
        if ($app.Process.HasExited) { return $null }
        $condition = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
        foreach ($element in @($app.Window.FindAll($Tree::Descendants, $condition))) {
            if (-not $element.Current.IsOffscreen) { return $element }
        }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $until)
    Write-Host "  not found: $id"
    $null
}

function Invoke-Element($element) {
    if ($element) { $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
}

function Close-InfoBar($app) {
    $bar = Find-Id $app 'ShellInfoBar' 2
    if ($bar) {
        $close = $bar.FindFirst($Tree::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, 'CloseButton')))
        Invoke-Element $close
    }
}

function Resize($app, [double]$width, [double]$height) {
    $transform = $null
    if ($app.Process.HasExited) { return }
    if ($app.Window.TryGetCurrentPattern([System.Windows.Automation.TransformPattern]::Pattern, [ref]$transform) -and $transform.Current.CanResize) {
        $transform.Resize($width, $height)
        Start-Sleep -Milliseconds 800
    }
}

function Wait-Until($app, [double]$second) {
    $left = $second - ((Get-Date) - $app.Process.StartTime).TotalSeconds
    if ($left -gt 0) { Start-Sleep -Milliseconds ([int]($left * 1000)) }
}

function Keep-FocusLog([string]$name) {
    $log = Join-Path $base "a11y-logs-$name\focus.log"
    if (Test-Path $log) { Copy-Item $log (Join-Path $results "focus-$name.log") }
}

# 1: first launch with an InfoBar that has an action, then without it, then Settings.
if (Want 'empty') {
    Write-Host 'Run: first launch'
    $app = Start-App ((Common 'empty') + '--missing-codec=heif')
    try {
        Find-Id $app 'ShellInfoBar' 15 | Out-Null
        Scan $app 'first-launch-infobar'
        Close-InfoBar $app
        Scan $app 'first-launch'
        Invoke-Element (Find-Id $app 'SettingsButton')
        Scan $app 'settings'
        Invoke-Element (Find-Id $app 'BackButton')
        Start-Sleep -Seconds 2
    }
    finally { Stop-App $app; Keep-FocusLog 'empty' }
}

# 2: an import of the broken test photos (the skipped-photos InfoBar), editing wide and each narrow tab.
if (Want 'editing') {
    Write-Host 'Run: import and editing'
    $app = Start-App ((Common 'editing') + "--motion-import=$broken" + '--motion-import-delay=1')
    try {
        Resize $app 1280 800
        $bar = Find-Id $app 'ShellInfoBar' 30
        if ($bar) {
            $buttons = $bar.FindAll($Tree::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)))
            Invoke-Element (@($buttons) | Where-Object { $_.Current.AutomationId -ne 'CloseButton' } | Select-Object -First 1)
            Scan $app 'editing-infobar'
            Close-InfoBar $app
        }
        else { Skip 'editing-infobar' 'no InfoBar appeared' }
        Scan $app 'editing-wide'
        Resize $app 900 760
        $tabs = Find-Id $app 'InspectorTabsControl' 8
        $radios = @(if ($tabs) { $tabs.FindAll($Tree::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::RadioButton))) })
        if ($radios.Count -eq 0) { Skip 'narrow tabs' 'the narrow layout did not show its tab row' }
        $names = 'narrow-timing', 'narrow-frame', 'narrow-output'
        for ($i = 0; $i -lt [Math]::Min($radios.Count, 3); $i++) {
            $radios[$i].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
            Scan $app $names[$i]
        }
        Resize $app 1280 800
        Start-Sleep -Seconds 2
    }
    finally { Stop-App $app; Keep-FocusLog 'editing' }
}

# 3: the export panel, the close question, the finished state wide and narrow.
if (Want 'export') {
    Write-Host 'Run: export'
    $export = Join-Path $base 'a11y-export.mp4'
    Remove-Item $export -ErrorAction SilentlyContinue
    $app = Start-App ((Common 'export') + "--sample-frames=$ExportPhotos" + '--output=2160' + "--export-to=$export")
    try {
        Resize $app 1280 800
        # The close question first (it only comes while the export runs; a finished export just closes the app), then the
        # panel: a fast GPU encoder can finish 400 frames in seconds.
        if (Find-Id $app 'CancelButton' 240) {
            $windowPattern = $null
            if ($app.Window.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern, [ref]$windowPattern)) { $windowPattern.Close() }
            $keep = Find-Id $app 'CloseButton' 5
            if ($keep) {
                Invoke-Scan $app 'close-question'
                Invoke-Element $keep
            }
            else { Skip 'close-question' 'the export had finished before the close step' }
        }
        if (-not $app.Process.HasExited -and (Find-Id $app 'CancelButton' 0.5)) {
            Invoke-Scan $app 'export-running'
        }
        else { Skip 'export-running' 'the export had finished before the scan' }
        Find-Id $app 'PlayButton' 240 | Out-Null
        Scan $app 'finished'
        Resize $app 900 760
        Scan $app 'finished-narrow'
    }
    finally { Stop-App $app; Keep-FocusLog 'export' }
}

# 4: the keyboard-only path through the shortcuts, run by Debug --shortcut-after at fixed seconds after launch, through
# the same method each keyboard accelerator calls (no key is pressed): Ctrl+I opens the folder picker (closed again here
# through UIA), Ctrl+N on a project with changes asks first (scanned, then Cancel), Ctrl+E exports (--export-to answers the
# Save dialog, --export-wait keeps it from starting by itself), Ctrl+N after the export opens the Empty screen, Ctrl+E
# there and Ctrl+I on the Settings page do nothing. focus-keys.log has each shortcut's result and every focus landing.
if (Want 'keys') {
    Write-Host 'Run: shortcuts'
    $export2 = Join-Path $base 'a11y-keys.mp4'
    Remove-Item $export2 -ErrorAction SilentlyContinue
    $app = Start-App ((Common 'keys') + '--sample-frames=400' + '--output=2160' + "--export-to=$export2" + '--export-wait' + '--shortcut-after=ctrl+i@8,ctrl+n@14,ctrl+e@26,ctrl+n@40,ctrl+e@43,ctrl+i@52')
    try {
        Resize $app 1280 800
        # Ctrl+I at 8 s: the folder picker is a second window of the process.
        $picker = $null
        $until = (Get-Date).AddSeconds(20)
        while (-not $picker -and (Get-Date) -lt $until -and -not $app.Process.HasExited) {
            Start-Sleep -Milliseconds 300
            $windows = $AE::RootElement.FindAll($Tree::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $app.Process.Id)))
            # The picker is a Win32 dialog: class #32770 on older builds; on newer ones (26300) another class, so any
            # window of the process that is not XAML (a ContentDialog is) and not the main window counts.
            $picker = @($windows) | Where-Object { $_.Current.ClassName -eq '#32770' -or ($_.Current.FrameworkId -ne 'XAML' -and $_.Current.NativeWindowHandle -ne $app.Window.Current.NativeWindowHandle) } | Select-Object -First 1
            if (-not $picker) { $picker = $app.Window.FindFirst($Tree::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, '#32770'))) }
            if (-not $picker) {
                $picker = @($app.Window.FindAll($Tree::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)))) |
                    Where-Object { $_.Current.FrameworkId -ne 'XAML' } | Select-Object -First 1
            }
        }
        if ($picker) {
            $summary.Add("ctrl+i opened '$($picker.Current.Name)' ($($picker.Current.ClassName)); closed through UIA")
            Write-Host "  Ctrl+I opened '$($picker.Current.Name)'"
            $picker.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
        }
        else { Skip 'ctrl+i picker' 'no picker window appeared' }
        # Ctrl+N at 14 s: the project has changes, so it asks first.
        Wait-Until $app 14
        if (Find-Id $app 'PrimaryButton' 8) {
            Invoke-Scan $app 'new-project-question'
            Invoke-Element (Find-Id $app 'CloseButton' 2)
        }
        else { Skip 'new-project-question' 'no question appeared' }
        # "24" typed into the Timing box and not committed (ValuePattern sets the text without focus or Enter), as a user
        # who types and presses Ctrl+E at once: the export must take it (#38 review, M1). 400 frames at 24 fps are 16.7 s,
        # at the default 30 fps 13.3 s. Typed at 23 s, after the focus walk of the screen the question left (19 s), whose
        # Tab walk through the box would commit it; focus-keys.log then shows the TimingChanged announcement at Ctrl+E.
        # The edit box itself: its label and the group around it carry the same name.
        Wait-Until $app 23
        $isBox = New-Object System.Windows.Automation.AndCondition(
            (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, 'Custom frames per second')),
            (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))
        $box = @($app.Window.FindAll($Tree::Descendants, $isBox)) |
            Where-Object { -not $_.Current.IsOffscreen } | Select-Object -First 1
        if ($box -and ((Get-Date) - $app.Process.StartTime).TotalSeconds -lt 25) {
            $box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('24')
            Start-Sleep -Seconds 1
            # The readout still says the stored rate: the entry is in the box only.
            $readout = @($app.Window.FindAll($Tree::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))) |
                Where-Object { $_.Current.Name -match ' fps' } | Select-Object -First 1
            $summary.Add("timing box before ctrl+e: '$($box.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value)' typed; readout '$(if ($readout) { $readout.Current.Name })'")
        }
        else { Skip 'ctrl+e typed rate' 'the Timing box was not found before Ctrl+E' }
        # Ctrl+E at 26 s, the finished state: its summary says the video's length.
        # (The transport's Play is also 'PlayButton', so the finished state is found by its title.)
        $title = Find-Id $app 'FinishedTitle' 60
        if ($title) {
            Start-Sleep -Seconds 1
            $finished = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetNextSibling($title)
            $summary.Add("ctrl+e finished: '$($title.Current.Name)', '$(if ($finished) { $finished.Current.Name })' (16.7 s means the typed 24 fps)")
        }
        # Ctrl+N at 40 s, Ctrl+E at 43 s on the Empty screen, then Settings.
        Wait-Until $app 45
        if (Find-Id $app 'ChooseFolderButton' 5) { $summary.Add('ctrl+n after the export: the Empty screen') }
        Invoke-Element (Find-Id $app 'SettingsButton' 2)
        # Ctrl+I at 52 s on the Settings page.
        Wait-Until $app 55
        Invoke-Element (Find-Id $app 'BackButton' 2)
        Start-Sleep -Seconds 2
    }
    finally { Stop-App $app; Keep-FocusLog 'keys' }
}

# 5: New project with an InfoBar open (#38 review, M2): the skipped-photos InfoBar of the broken test photos stays open,
# Ctrl+N at 15 s asks first, the question is confirmed through UIA, and focus-newproject.log must show the Empty screen's
# focus on Choose folder, not on the InfoBar's Dismiss.
if (Want 'newproject') {
    Write-Host 'Run: New project with an InfoBar open'
    $app = Start-App ((Common 'newproject') + "--motion-import=$broken" + '--motion-import-delay=1' + '--shortcut-after=ctrl+n@15')
    try {
        Resize $app 1280 800
        if (Find-Id $app 'ShellInfoBar' 14) { $summary.Add('newproject: the skipped-photos InfoBar is open before Ctrl+N') }
        Wait-Until $app 15
        $confirm = Find-Id $app 'PrimaryButton' 8
        if ($confirm) {
            $summary.Add("newproject: the question asked; confirmed '$($confirm.Current.Name)'")
            Invoke-Element $confirm
        }
        Start-Sleep -Seconds 4
    }
    finally { Stop-App $app; Keep-FocusLog 'newproject' }
    $log = Join-Path $results 'focus-newproject.log'
    if (Test-Path $log) {
        $landing = Select-String -Path $log -Pattern '^\s*\d+ screen: Empty' -Context 0, 1 | Select-Object -Last 1
        $summary.Add("newproject: $(if ($landing) { ($landing.Line.Trim() + ' / ' + $landing.Context.PostContext[0].Trim()) } else { 'no Empty screen in the focus log' })")
    }
}
Remove-Item (Join-Path $base 'a11y-export.mp4'), (Join-Path $base 'a11y-keys.mp4') -ErrorAction SilentlyContinue

$summary | Set-Content (Join-Path $results 'summary.txt')
Write-Host "Results in $results"
if ($skipped.Count -gt 0) { Write-Host "Skipped: $($skipped -join '; ')" }
foreach ($name in 'empty', 'editing', 'export', 'keys', 'newproject') {
    Get-ChildItem (Join-Path $base "a11y-logs-$name") -Filter '10Lapse-*.log' -ErrorAction SilentlyContinue |
        ForEach-Object { Select-String -Path $_.FullName -Pattern ' (WARN|ERROR|CRIT) ' | ForEach-Object { "$name`: $($_.Line)" } }
}

if ($Clean) {
    Get-ChildItem $base -Exclude 'a11y' | Remove-Item -Recurse -Force
    Write-Host "Removed the test data under $base (kept a11y)"
}

if ($notScanned.Count -gt 0) {
    Write-Host "NOT SCANNED (the scan could not run twice): $($notScanned -join ', ')"
    exit 1
}
