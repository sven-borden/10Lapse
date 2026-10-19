# Capture-time fixtures (#84, #85)

Real camera and app files, each cut to the first bytes the capture-time reader needs: the container headers,
the metadata boxes or segments and the Exif block. No image data is left, apart from the small thumbnail some
formats keep inside their metadata. `CaptureTimeFixtureTests` reads every file and checks the time against the
value exiftool 13.59 and MetadataExtractor 2.9.3 gave for the whole file. The test also fails when a file is
missing from this table, or the table names a file that is not here.

Only sources whose licence allows a copy in this repository are used:

- **metadata-extractor-images** (<https://github.com/drewnoakes/metadata-extractor-images>), whose README says:
  "You are free to use these media files however you wish."
- **raw.pixls.us** (<https://raw.pixls.us/>), which publishes its samples under CC0 1.0 (public domain).

exiftool's and exiv2's test images are not used: their terms (Perl's Artistic licence or the GPL, or none
stated) do not fit this repository.

Each file is a prefix of the original, cut where the reader stops reading; for HEIF and CR3, where a box or an
Exif item must be whole, at the end of that box or item. The scratch tool that cut them is not committed;
`head -c <bytes>` of the original gives the same file.

| File | Source | Bytes | What it covers | Expected time |
|---|---|---|---|---|
| `jpeg-apple-iphone-6-plus.jpg` | metadata-extractor-images `jpg/Apple iPhone 6 plus.jpg` | 2,086 | JPEG Exif APP1 from a phone | 2014-10-31 13:32:37.539 |
| `nef-nikon-d90-big-endian.nef` | metadata-extractor-images `nef/Nikon D90.nef` | 928 | big-endian (`MM`) TIFF-based RAW | 2011-04-25 01:55:46 |
| `nef-nikon-d850.nef` | raw.pixls.us `Nikon/D850/Nikon-D850-12bit-compressed.NEF` | 13,423 | little-endian NEF with sub-seconds and an offset | 2017-10-15 17:35:20.41 +01:00 |
| `dng-apple-iphone-12-pro.dng` | raw.pixls.us `Apple/iPhone 12 Pro/IMG_1361.DNG` | 3,227 | big-endian DNG (ProRAW) | 2020-12-29 14:24:45.7 -05:00 |
| `cr3-canon-eos-m200.cr3` | raw.pixls.us `Canon/Canon EOS M200/_MG_2231.CR3` | 25,840 | CR3: `moov`, Canon `uuid`, `CMT2` | 2025-08-09 13:27:20.94 +11:00 |
| `heic-apple-iphone-img-2927.heic` | metadata-extractor-images `heic/IMG_2927.HEIC` | 5,367 | iPhone HEIC, `iloc` version 1 | 2020-05-04 18:39:11.644 -04:00 |
| `raf-fujifilm-finepix-s5500.raf` | metadata-extractor-images `raf/FujiFilm FinePix S5500.raf` | 10,658 | RAF cut short: the header promises more JPEG than the file holds | 2013-02-09 10:32:22 |
| `png-ztxt-raw-profile-issue-316.png` | metadata-extractor-images `png/Issue 316 (dotnet).png` | 10,701 | PNG Exif as an ImageMagick raw profile in `zTXt` | 2012-08-23 17:42:23.61 |

The other PNG raw-profile forms (`tEXt`, `iTXt` plain and compressed, the `APP1` keyword) and the HEIF `iloc`
variants no sample file uses (version 2, index fields, 8-byte fields, a base offset, several extents, an item in
`idat`) are built by hand in `ExifFiles` and tested in `CaptureTimeReaderTests.ReadsEveryContainer`.
