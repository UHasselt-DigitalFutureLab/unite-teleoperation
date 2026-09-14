<div align="center">

# UNITE: A Modular Testbed for Reproducible Delayed Space Teleoperation [SpaceCHI 2026]

---

[Dries Cardinaels](https://orcid.org/0009-0002-1255-0838)<sup>1</sup> &nbsp;&middot;&nbsp;
[Thomas Pietrzak](https://orcid.org/0000-0002-2013-7253)<sup>2</sup> &nbsp;&middot;&nbsp;
[Raf Ramakers](https://orcid.org/0000-0001-6466-0663)<sup>1</sup> &nbsp;&middot;&nbsp;
[Kris Luyten](https://orcid.org/0000-0002-4194-1101)<sup>1</sup>

<sup>1</sup> Digital Future Lab, UHasselt - Flanders Make, Diepenbeek, Belgium
&nbsp;&middot;&nbsp;
<sup>2</sup> Univ. Lille, Inria, CNRS, Centrale Lille, UMR 9189 CRIStAL, Lille, France

[![SpaceCHI](https://img.shields.io/badge/SpaceCHI-2026-6f42c1)](https://spacechi.media.mit.edu)
[![License](https://img.shields.io/badge/license-PolyForm--NC-2ea44f)](LICENSE)
[![Documentation](https://img.shields.io/badge/documentation-unite.driescardinaels.be-4c6ef5)](https://unite.driescardinaels.be)

[![Engine](https://img.shields.io/badge/Unity-6000.3.15f1-222c37?logo=unity)](https://unity.com/)
[![Assets](https://img.shields.io/badge/Assets-Git_LFS-f64935?logo=git)](https://git-lfs.com/)
[![Cite](https://img.shields.io/badge/cite-CITATION.cff-lightgrey)](CITATION.cff)

</div>

UNITE is a source-available research testbed for configurable, reproducible
human-in-the-loop mobile-robot teleoperation under communication delay.
It separates operator input, communication, vehicle behavior, feedback,
assistance, task logic, and data capture into explicit modules.

<picture>
  <source media="(prefers-color-scheme: dark)" srcset=".github/assets/telerobotic-loop-dark.png">
  <source media="(prefers-color-scheme: light)" srcset=".github/assets/telerobotic-loop-light.png">
  <img
    src=".github/assets/telerobotic-loop-light.png"
    alt="The UNITE telerobotic loop, from operator input through uplink communication to the remote rover and back through downlink feedback"
    width="100%"
  >
</picture>

## Start here

**[Read the documentation](https://unite.driescardinaels.be)**

The documentation is the canonical guide for installation, setup, scene
configuration, extension, and reproduction. This README is intentionally only
an overview.

## What is included

- A reusable `Core` and `Kernel` for staged teleoperation loops.
- A Unity reconstruction of the *Every Move You Make* lunar-rover study.
- Configurable uplink/downlink communication and operator-side assistance.
- Typed contracts, condition scenes, logging, and citation metadata.

## Requirements

- Unity `6000.3.15f1`
- Git and Git LFS

Git LFS is required, not optional. Cloning without it installed produces
pointer files instead of the lunar terrain and rover meshes, and those meshes
are what the rover drives on, what the boundary guard tests for slope, and what
the trajectory visualisations project onto. Without them the condition scenes
do not run and the study cannot be recreated.

Follow the documentation before opening or modifying the project.

## Repository map

| Path | Purpose |
| --- | --- |
| `Assets/UNITE/Core/` | Shared contracts and extensible types |
| `Assets/UNITE/Kernel/` | Stable module lifecycle and pipeline infrastructure |
| `Assets/UNITE/Demo/` | Study-specific implementations, scenes, configurations, and art |
| `CITATION.cff` | Citation metadata |
| `THIRD_PARTY_NOTICES.md` | Asset provenance and licensing boundaries |

## Cite

See [`CITATION.cff`](CITATION.cff) for the machine-readable citation record.

Corresponding author: [Dries Cardinaels](mailto:dries.cardinaels@uhasselt.be).

## License

Copyright &copy; 2026 Hasselt University, Digital Future Lab.

Original UNITE source code and project files are licensed under the
[PolyForm Noncommercial License 1.0.0](LICENSE). Any noncommercial purpose is
permitted, and the license explicitly covers use by educational institutions
and public research organizations regardless of their source of funding.
Commercial use requires a separate license from Hasselt University, which
holds the copyright. Authorship is recorded separately in
[`CITATION.cff`](CITATION.cff).

PolyForm Noncommercial is a source-available license, not an OSI-approved
open-source license. Bundled external assets may have separate terms; see
[`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).
