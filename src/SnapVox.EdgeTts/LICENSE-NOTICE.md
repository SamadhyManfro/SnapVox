# License of this library

`SnapVox.EdgeTts` contains code translated to C# from the **edge-tts** project
(https://github.com/rany2/edge-tts), in particular `src/edge_tts/drm.py` (generation of the
`Sec-MS-GEC` v2 token) and the protocol from `src/edge_tts/communicate.py`.

edge-tts is distributed under the **GNU Lesser General Public License v3.0 (LGPL-3.0)**, and this
library follows the same license. Full texts in this folder: `COPYING.LESSER` (LGPL-3.0) and
`COPYING` (GPL-3.0, which the LGPL supplements).

The rest of SnapVox is distributed under the GPL-3.0 (see `LICENSE` at the repository root),
which is compatible with this library.

Note: Microsoft Edge's "Read aloud" service is neither an official nor a documented API. It may
change or stop working at any time. That's why SnapVox automatically falls back to the Piper
voices (local) and then to the Windows voice when Edge fails.
