# SistemaMcp user guide

[Deutsche Fassung](guide.de.md) · [Back to README](../README.md)

This guide explains how SistemaMcp works, how to use it best and where its limits are. It is written for design
engineers, electrical planners and safety engineers who know SISTEMA.

## Contents

1. [Basic idea](#1-basic-idea)
2. [From conversation to SISTEMA project](#2-from-conversation-to-sistema-project)
3. [What the assistant needs from you](#3-what-the-assistant-needs-from-you)
4. [Component libraries](#4-component-libraries)
5. [How the structure maps to SISTEMA](#5-how-the-structure-maps-to-sistema)
6. [Modelling patterns from practice](#6-modelling-patterns-from-practice)
7. [Questions, errors, warnings, assumptions](#7-questions-errors-warnings-assumptions)
8. [Estimate and check in SISTEMA](#8-estimate-and-check-in-sistema)
9. [Troubleshooting](#9-troubleshooting)
10. [Roadmap](#roadmap)

---

## 1. Basic idea

SISTEMA is the calculation tool; SistemaMcp bridges your description of the machine and a SISTEMA project. The server

- **never invents safety values.** PLr, category, MTTFd/B10D, DC, CCF and the confirmation of the category
  requirements come from you or from a manufacturer library. Anything missing becomes a question.
- **documents the source.** Library, version, manufacturer and use case are written into the SISTEMA project; DC
  values carry the Annex E measure as justification.
- **leaves the calculation to SISTEMA.** Its own estimate only helps to catch mistakes early.

The assistant (e.g. Claude) translates your input into a JSON specification ([format](spec-format.md)), calls the
tools and asks you the questions. You don't have to look at the specification, but you can ask for it at any time,
save it and reuse it.

## 2. From conversation to SISTEMA project

| Step | What happens | Tool |
|---|---|---|
| 1 | You describe the machine, hazards, components | – |
| 2 | The assistant looks up the components in the libraries | `library_search`, if needed `library_download` / `library_import` |
| 3 | It drafts the safety functions and has them checked | `sistema_validate_spec` |
| 4 | It asks you **all** open questions at once | – |
| 5 | Steps 3–4 repeat until nothing is missing | `sistema_validate_spec` |
| 6 | The project is written | `sistema_create_project` |
| 7 | SISTEMA opens; you check and save with *File ▸ Save as* | `sistema_open_in_sistema` |
| 8 | The assistant reads SISTEMA's PL/PFH and compares them with the PLr | `sistema_read_project` |

Two to three rounds of questions are typical. The more complete your first description, the fewer rounds.

## 3. What the assistant needs from you

| Information | Example | Used for |
|---|---|---|
| Project name, machine, author | "Packaging PM-3, author J. Doe" | project header |
| **Bill of materials** of the safety-related parts | part numbers, quantities | find devices, derive safety functions |
| **PLr per safety function** | "PLr d from the risk assessment" or S/F/P | requirement |
| **Stop category / reaction** | "large axis SS1, small axis STO" | reaction, safe state, use case |
| **Wiring concept** | "e-stop 2-channel at field box, STO 2-channel, axis with safety encoder" | category, use case, DC |
| **Diagnostics** | "cross-circuit detection by test pulses", "feedback loop" | DC |
| **CCF measures** | "standard measures fulfilled" or individual measures | ≥ 65 points (cat. 2–4) |
| **Operating profile** | "250 days, 2 shifts × 8 h, door 4× per hour" | nop → MTTFd, T10D |
| Mission time | default 20 years | characteristic values |
| Identical functions | "all 7 doors wired the same way" | representative safety function |

A good first message:

> Project "PM-3", author J. Doe. PLr d for all functions. BOM below. All sensors are connected to two Schmersal SFB
> field boxes (PROFIsafe) on an F-CPU 1517F. Drives SEW MOVIDRIVE modular with CSS21A safety card; large axis with
> safety encoder, SS1; small axis STO. STO 2-channel. E-stop 2-channel, test pulses with cross-circuit detection.
> Standard CCF measures fulfilled. Operation 250 days × 16 h, doors about 4× per hour, e-stop test once a week. Model
> identically wired doors and e-stops representatively. Libraries are in D:\Safety\VDMA.

## 4. Component libraries

### 4.1 Recommendation: provide the libraries yourself

Only manufacturers with a fixed file link can be downloaded automatically. Most large manufacturers require a browser
(JavaScript portal, login, licence agreement) and block scripts. Therefore:

1. Create a folder, e.g. `D:\Safety\VDMA`, with one sub-folder per manufacturer.
2. Download and extract the libraries of your preferred components (`library_list_manufacturers` lists the pages).
3. Set the folder as `SISTEMA_MCP_LIBRARY_PATHS` in the MCP configuration – or copy it once into the local cache
   with `library_import`.
4. Update libraries deliberately (e.g. twice a year) and record which version applies to a project. The version is
   also stored in the SISTEMA project.

Benefits: reproducible results, no dependency on manufacturer websites, you decide on the version.

### 4.2 What is read

| Format | Content | Use |
|---|---|---|
| VDMA 66413 XML (also inside ZIP) | devices with use cases | type 1 (PL + PFH) and type 4 (PL only) → subsystem; type 2 (MTTFd, λ, MTTF/MTBF) and type 3 (B10D/B10) → element |
| SISTEMA library `.slb` | Firebird database | subsystems with PL/PFH entered directly, blocks, elements |

A device often has **several use cases** (e.g. field box: input 2-channel PL e / 1-channel PL d / output via 1 or 2
lines). Which one applies depends on your wiring – the server asks if it is ambiguous.

### 4.3 Searching

`library_search` matches type, part number, name, group and text – independent of spaces and hyphens. Order codes
from a BOM are traced back to the type: `AZM201Z-I2-ST2-T-1P2PW` finds "AZM 201" (and related types such as
"AZM 201 D" – you choose the right one). The part number from your BOM is written into the project even if the
library has none.

Contradictions are reported: a PL the category does not allow (B → at most b, 1 → c, 2 → d) is an error for your own
values and a warning for library values.

## 5. How the structure maps to SISTEMA

| Specification | SISTEMA | Note |
|---|---|---|
| `safetyFunctions[]` | SF | PLr entered directly; a risk graph is evaluated and documented as text |
| subsystem with `library` (type 1/4) or `direct` | SB, PL/PFH "entered directly" | manufacturer, library and use case are taken over |
| subsystem with `category` + `channels` | SB "from sub-items" | channel 1, channel 2, test channel |
| component in `ch1`/`ch2`/`test` | BL with exactly one EL | MTTFd directly or B10D + nop; DC as value with justification |
| `ccfMeasures` | CCF measures (CCFMID-001…008) | points according to Annex F |
| `categoryRequirementsMet` | category requirement check boxes | pre-set for categories 1 and 3 |

The file is written the way SISTEMA writes it (field list of ssm 3.0.0, UTF-8, date format, decimal comma).
Calculated fields stay empty; SISTEMA fills them in.

## 6. Modelling patterns from practice

**Model identical functions representatively.** Seven identically wired doors → one safety function "guard door" with
the note "7 pieces, representative". If doors are evaluated differently or act on different drives, create separate
safety functions.

**Safety controller as a chain.** Sensor → field box / F-I/O → F-CPU → drive: every device with PL/PFH is its own
subsystem. PFH values are summed.

**E-stop built from contacts.** E-stop devices usually only give the B10D of the NC contacts. The e-stop is then a
subsystem built from components: category (usually 3), two NC contacts in channels 1 and 2, DC from the evaluation
(test pulses, cross-circuit detection), CCF, frequency of operation including tests.

**One safety module for several axes.** If a safety card (e.g. SEW CSS21A) controls several axes, the card appears
**once** in the chain, followed by the power sections of the axes (STO). Its PFH is not counted twice.

**Resolve uncertainty conservatively.** If something is open (DC value, operating time, 1- or 2-channel), the
assistant takes the less favourable variant and says so. You correct it later.

## 7. Questions, errors, warnings, assumptions

| Kind | Meaning | Example |
|---|---|---|
| **Question** | information missing – no project is generated | "How often does the component switch?", "Which use case?" |
| **Error** | contradicts the standard or invalid – no project is generated | CCF 40 < 65, DC 150 %, category 3 without channel 2 |
| **Warning** | project is generated, but something needs attention | T10D < mission time (replacement), PFH does not match PL |
| **Assumption** | default of the standard used | mission time 20 years |

`sistema_list_measures` lists the valid keys for CCF and DC measures with points and DC values.

## 8. Estimate and check in SISTEMA

The estimate calculates MTTFd per channel (capped, symmetrised), DCavg, CCF and the PL according to the simplified
procedure. Subsystems built from components have no PFH in it; if a safety function contains several PL d
subsystems, it combines PL levels and is then often one step below SISTEMA. This is intentionally cautious.

**SISTEMA is authoritative:**
1. Open the file (`sistema_open_in_sistema`), read the messages.
2. Spot-check category check boxes, library data, texts.
3. *File ▸ Save as* (same name) – only then are the results stored in the file.
4. `sistema_read_project` returns PL/PFH per safety function and whether the PLr is reached.

## 9. Troubleshooting

| Problem | Solution |
|---|---|
| `sistema_status`: SISTEMA not found | set `SISTEMA_INSTALL_DIR` |
| SLB: "needs the 32-bit server build" | use the release exe (win-x86) |
| `library_download`: "manual-download-required" | download in the browser, `library_import` |
| device not found | search for the type stem; `library_list_sources` shows read errors |
| SISTEMA: "file could not be opened" | open an issue with the specification; do not edit the file manually |
| results missing after opening | use *Save as* in SISTEMA |

## Roadmap

Planned or derived from the field tests:

- define devices once and reuse them in several safety functions (fewer duplicate questions)
- PFH approximation for subsystems built from components in the estimate
- automatic discovery of file links on manufacturer pages
- category check boxes for B, 2 and 4; DC as SISTEMA measure
- extending and changing existing projects
- EMI and software requirements (ISO 13849-1:2023)

Feedback and contributions are welcome (issues / pull requests).
