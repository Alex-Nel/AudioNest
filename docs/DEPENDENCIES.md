# Audio dependency and distribution notes

- `SoundFlow` and `SoundFlow.Codecs.FFMpeg` are pinned to **1.4.2** and use MIT
  licenses for managed code and the native wrappers.
- MiniAudio remains responsible for devices and WAV/MP3/FLAC import. FFmpeg adds
  AAC/M4A/OGG decoding and MP3/AAC/OGG/FLAC encoding. No separately installed
  ffmpeg executable is invoked.
- The codec package reports **FFmpeg LGPL 2.1 or later**, statically linked inside
  the separately loaded `soundflow-ffmpeg` native library, with GPL/nonfree disabled.
  The native build also uses LAME for MP3; retain its applicable LGPL notices.
- Build/publish copies the package's unmodified license and third-party notices
  into `licenses/`. These notices are upstream disclosures, not a complete
  release-compliance bundle or a legal determination.
- Before binary distribution, review the exact native build and all bundled
  components, provide the applicable license texts and corresponding source/
  modifications, and meet LGPL relinking requirements (including build materials
  or relinkable objects where applicable). A generic upstream source link alone
  does not establish compliance. Do not ship only the application executable:
  include native libraries and notices, and preserve users' replacement/relinking
  rights.

Upstream source/build definitions:
The package metadata identifies source revision
[`0a1f2c98ba13fa40fffe720fafe60d8f52ae81fd`](https://github.com/LSXPrime/SoundFlow/tree/0a1f2c98ba13fa40fffe720fafe60d8f52ae81fd);
see its `Native/ffmpeg-codec` build definitions, plus
<https://ffmpeg.org/legal.html>, and <https://lame.sourceforge.io/>.

Windows x64 codec round trips are tested locally; other operating systems and
release packaging still require their own native-deployment and compliance review.
