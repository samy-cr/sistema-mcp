# SistemaMcp – MCP server for IFA SISTEMA

[Deutsch](README.de.md) · [User guide](docs/guide.en.md) · [Example project](examples/packaging-machine/README.md) · [Specification format](docs/spec-format.md)

SistemaMcp connects an AI assistant (Claude or any other [MCP](https://modelcontextprotocol.io) client) with
[IFA SISTEMA](https://www.dguv.de/ifa/praxishilfen/praxishilfen-maschinenschutz/software-sistema/index.jsp), the free
tool for evaluating safety-related parts of control systems according to **ISO 13849-1**.

You describe the machine in your own words – bill of materials, risk assessment, wiring concept – and the assistant
builds a complete SISTEMA project from it: safety functions, subsystems, channels, components with characteristic
values from the manufacturers' **VDMA 66413 libraries**. Whatever is missing is **asked, never guessed**. The result
opens in SISTEMA, where the authoritative calculation takes place.

> **Disclaimer.** Not affiliated with or endorsed by IFA or DGUV. SistemaMcp does not replace the judgement of a
> competent person. Every generated project must be checked in SISTEMA. No IFA or manufacturer data is distributed
> with this software.

---

## What it does

| | |
|---|---|
| **Generate projects** | From a JSON specification that the assistant writes for you: safety functions, PLr (directly or via risk graph), subsystems with category, channels, CCF, DC, MTTFd/B10D, mission time |
| **Ask instead of guess** | The validator turns every gap into a precise question – PLr, category, CCF measures, diagnostic coverage, switching frequency, which use case of a device applies … |
| **Check plausibility** | MTTFd per channel (symmetrisation, capping), DCavg, CCF score, T10D, a conservative PL estimate, contradictions such as PFH ≠ PL |
| **Use manufacturer data** | Reads VDMA 66413 libraries (XML/ZIP) and SISTEMA libraries (`.slb`), searches by type or part number, takes over PL/PFH, MTTFd, B10D with source information |
| **Round trip with SISTEMA** | Opens the file in SISTEMA and reads back the PL/PFH that SISTEMA calculated |
| **Read existing projects** | Complete tree of any `.ssm` file for reviews and questions like "which safety function misses its PLr?" |

## How it works

```
 You (chat)                      AI assistant                       SistemaMcp                    SISTEMA
 ─────────────                   ────────────                       ──────────                    ───────
 BOM, risk assessment,  ───►  writes JSON specification   ───►  sistema_validate_spec
 wiring concept                                                    │  questions / errors / estimate
                         ◄───  asks you all open questions  ◄──────┘
 answers                ───►  completes the specification  ───►  library_search / _import (VDMA, SLB)
                                                            ───►  sistema_create_project ──► .ssm file
                                                            ───►  sistema_open_in_sistema ─────────────► calculates PL/PFH
 check, "Save as"                                                                                    │
                         ◄───  reports official results     ◄──  sistema_read_project ◄─────────────┘
```

## Recommended way of working

1. **Provide the libraries yourself.** Most manufacturers (Siemens, SEW, SICK, Pilz, Phoenix Contact …) publish their
   VDMA 66413 files only behind web portals. Download the libraries for your preferred components once, keep them in
   one folder and point `SISTEMA_MCP_LIBRARY_PATHS` at it (or ask the assistant to `library_import` them). That
   makes every later project faster and reproducible – and you decide which library version is used.
2. **Give the assistant good input:** bill of materials, PLr per hazard from the risk assessment, stop categories,
   wiring concept (1- or 2-channel, encoder, which device switches what) and the operating profile (days, hours,
   cycles).
3. **Answer the questions, don't let the assistant fill gaps.** If it proposes a value, it says so – confirm or
   correct it.
4. **Check in SISTEMA, save with *File ▸ Save as*,** then let the assistant read back the results.

The [example project](examples/packaging-machine/README.md) shows a complete session with a packaging machine
(Schmersal, Siemens F-CPU, SEW MOVIDRIVE) including the questions, the assumptions and suggested optimisations.

## Requirements

- Windows with **SISTEMA 3.0.x** installed (tested with 3.0.4, ISO 13849-1:2023)
- An MCP client, e.g. Claude Code or Claude Desktop

The release is self-contained (no .NET installation needed). It runs as a 32-bit process because SISTEMA ships a
32-bit embedded Firebird engine, which is used to read `.slb` libraries.

## Installation

1. Download `SistemaMcp-win-x86.zip` from the [releases](../../releases) and extract it, e.g. to
   `C:\Tools\SistemaMcp`.
2. Register the server:

   **Claude Code**
   ```
   claude mcp add sistema --scope user -- C:\Tools\SistemaMcp\SistemaMcp.exe
   ```
   **Claude Desktop** (`claude_desktop_config.json`)
   ```json
   {
     "mcpServers": {
       "sistema": {
         "command": "C:\\Tools\\SistemaMcp\\SistemaMcp.exe",
         "env": { "SISTEMA_MCP_LIBRARY_PATHS": "D:\\Safety\\VDMA-Libraries" }
       }
     }
   }
   ```
3. Ask the assistant: *"Check the SISTEMA MCP status."*

| Environment variable | Meaning |
|---|---|
| `SISTEMA_MCP_LIBRARY_PATHS` | Folders/files with VDMA XML, extracted ZIPs or `.slb`, separated by `;` – **recommended** |
| `SISTEMA_INSTALL_DIR` | SISTEMA folder, if not found automatically |
| `SISTEMA_MCP_DATA_DIR` | Cache for downloaded/imported libraries (default `%LOCALAPPDATA%\SistemaMcp`) |

## Quick start

> *"Create a SISTEMA project 'Palletiser PL-2'. Emergency stop with Schmersal BDF200 on a Siemens F-CPU 1516F,
> guard door with AZM300, drives SEW MOVIDRIVE with STO. PLr d everywhere. The libraries are in D:\Safety\VDMA."*

The assistant imports the libraries, finds the devices, proposes the safety functions and asks what is still
missing – for example the category of the emergency stop input, the CCF measures and how often it is operated.

## Tools

| Tool | Purpose |
|---|---|
| `sistema_status` | Installation, folders, library status, recommended workflow |
| `sistema_get_spec_format` | JSON format of a project specification with example |
| `sistema_list_measures` | Keys for CCF measures (Annex F), DC measures (Annex E), category requirements, risk graph |
| `sistema_validate_spec` | Questions, errors, warnings and a conservative estimate – writes nothing |
| `sistema_create_project` | Writes the `.ssm` file (refuses incomplete specifications, never overwrites silently) |
| `sistema_open_in_sistema` | Opens a file in SISTEMA |
| `sistema_read_project` | Reads a project including the results SISTEMA stored |
| `sistema_list_projects` | Lists `.ssm` files in a folder |
| `library_list_manufacturers` | Manufacturers with VDMA 66413 libraries (VDMA reference list) and their download pages |
| `library_download` | Downloads a library from a direct file link into the local cache |
| `library_import` | Imports a ZIP / XML / SLB or a folder you downloaded |
| `library_search`, `library_get_component` | Search devices by type, part number, text; show all use cases |
| `library_list_sources` | Indexed sources and read errors |

## Component libraries

The [VDMA reference list](https://www.vdma.eu/66413bibo) names about 40 manufacturers. Only a few publish a stable
direct file link (Bernstein, Camozzi, Eaton, ENGEL, Turck, TWK, Wittenstein; Schmersal via the link on its product
page). Siemens, SEW, SICK, Pilz, Phoenix Contact, Festo and most others require a browser (search, login, licence
agreement). Tested imports: Schmersal components V9 (144 devices), Siemens 1.22.0 (982), SEW 2022-12 (144),
Turck 2024 (16). Libraries stay on your machine; please respect the manufacturers' terms of use.

## About the estimate

`sistema_validate_spec` uses the *simplified procedure* of ISO 13849-1 and is deliberately conservative – e.g. a
subsystem built from components has no PFH in the estimate, so safety functions with several PL d subsystems may be
estimated one step lower than SISTEMA calculates. Use it to catch mistakes early; the result that counts is SISTEMA's.

## Limitations (v0.1)

- One block with one element per component; no editing of existing projects (generation only).
- Category requirement check boxes are pre-set only for categories 1 and 3; tick them in SISTEMA for B, 2, 4.
- DC is written as a value with justification, not as a SISTEMA measure id.
- The risk graph is evaluated and documented, the PLr is stored as "entered directly".
- `.slb` subsystems are used when their PL is entered directly; EMI (ISO 13849-1:2023) and software (SRESW/SRASW)
  are not filled in.
- SISTEMA does not always store results on load alone – use *File ▸ Save as…*.

See the [roadmap in the user guide](docs/guide.en.md#roadmap).

## Building from source

```
dotnet test tests/SistemaMcp.Tests
dotnet publish src/SistemaMcp -c Release -o publish/SistemaMcp -p:PublishSingleFile=true
.\scripts\run-scenarios.ps1        # 30 end-to-end scenarios over the MCP protocol
```

## Privacy

The server works locally. It accesses the network only when `library_download` is called. No telemetry.

## License

[MIT](LICENSE). SISTEMA is a product of the Institute for Occupational Safety and Health of the German Social Accident
Insurance (IFA). VDMA 66413 is a specification of VDMA e.V. Product names belong to their respective owners.
