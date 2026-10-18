# Example: packaging machine

[Deutsch](README.de.md) · [Specification](spec.json) · [User guide](../../docs/guide.en.md)

This example is based on a real test session with SistemaMcp (anonymised, shortened to three safety functions; the
project texts are German because the machine was specified in German). The starting point was only a bill of
materials and "PLr d from the risk assessment". It shows how the conversation goes, which questions come up, which
assumptions are made – and what can be improved in the machine and in the way of working.

## 1. Starting point

| Part number | Qty | Device |
|---|---|---|
| AZM201Z-I2-ST2-T-1P2PW | 7 | Schmersal AZM201Z (guard door monitoring) |
| BDF100-NH-G-ST | 5 | Schmersal control station with emergency stop |
| RSS260-D-LSTM12-8-0,25M | 1 | Schmersal RFID safety sensor |
| SFB-PN-IRT-8M12-IOP-V2 | 2 | Schmersal safety field box (PROFINET/PROFIsafe) |
| 6ES7517-3FP00-0AB0 | 1 | Siemens F-CPU 1517F-3 PN/DP |
| MDA90A-0640-503-X-S00 | 1 | SEW MOVIDRIVE modular single axis (large) |
| MDD90A-0040-503-X-S00 | 1 | SEW MOVIDRIVE modular double axis (small) |
| CSS21A | 1 | SEW MOVISAFE safety card (PROFIsafe) |

First message: *"Project packaging. PLr d from the risk assessment. Derive the safety functions from the bill of
materials yourself. (BOM) SEW is only controlled via the safety card over PROFIsafe."*

## 2. The previous way (manually in SISTEMA)

1. Derive safety functions from BOM and circuit diagram.
2. Find, download and import the VDMA library of each manufacturer into SISTEMA.
3. Create SF, subsystems, channels, blocks and elements by hand.
4. Pick the right use case of each device.
5. Enter category, CCF, DC, switching cycles, category check boxes for each subsystem.
6. Repeat identical chains for every SF (here three times field box, F-CPU, SEW).
7. Check, create the report.

Typical mistakes: missing check boxes, wrong use case, inconsistent values between similar SF, missing
justifications, T10D overlooked.

## 3. With SistemaMcp

**Round 1 – draft from the BOM.** The assistant maps the part numbers and proposes the safety functions: emergency
stop, guard door (stop when opened), RFID-monitored flap – each chain sensor → field box → F-CPU → CSS21A → axes. The
server lists what is missing (reaction, safe state, PL/PFH/category of each device); the assistant also asks STO or
SS1 and whether identically wired doors and e-stops should be modelled representatively.

**Libraries.** Schmersal: file link found on the product page, `library_download` (components V9, 144 devices).
Siemens and SEW: portals block scripts → the user downloads, `library_import` (Siemens 1.22.0 with 982 devices, SEW
2022-12 with 144). All devices are found directly by the BOM order code (e.g. `AZM201Z-I2-ST2-T-1P2PW` → "AZM 201").

**Round 2 – answers.** E-stop on the field box with 2-channel evaluation, cat. 3, CCF fulfilled, operated once per
hour; STO for the small axis, SS1 for the large one. The assistant states its **assumptions**: CCF 70 points
(separation, over-voltage, well-tried, EMC, environment); DC 90 % (cross monitoring with test pulses) instead of 99 %;
24/7 operation → **T10D 11.4 years** (replace e-stop devices).

**Round 3 – drive wiring.** Large axis with encoder, small axis via CSS21A → the CSS21A appears once in the chain,
followed by MDA and MDD with STO (1-channel as the less favourable assumption).

**Result.** Complete, no errors, T10D warning. Estimate: SF-02 PL d (PFH 1.45E-8), SF-03 PL d (1.33E-8); SF-01
estimated PL c only because the e-stop subsystem built from contacts has no PFH in the estimate – SISTEMA calculates
it exactly and is authoritative.

## 4. Suggested improvements of the machine (for review by the designer)

| Topic | Today | Suggestion | Effect |
|---|---|---|---|
| STO | 1-channel assumed | confirm 2-channel | margin up to PL e |
| DC e-stop | 90 % assumed | confirm cross-circuit detection → 99 % | higher DCavg |
| Operating profile | 24/7 assumed | real days/hours | realistic T10D and maintenance interval |
| E-stop replacement | – | T10D in manual/maintenance plan | required if T10D < mission time |
| Representative SF | 7 doors / 5 e-stops as one SF each | document that all are wired identically and act on both axes | traceable validation |
| Axes | both axes in every SF | if a door acts on one axis only, one SF per axis | less conservative, clearer |

## 5. Improving the way of working

Provide the libraries in advance, include the wiring concept (1/2-channel, encoder, who switches what), the operating
profile and the stop category per drive. With that, this project would have been complete in **one** round.

## 6. Reproduce

```
library_download  <Schmersal ZIP link from the product page "SISTEMA VDMA components">
library_import    <Siemens library folder>   id = siemens
library_import    <SEW library folder>       id = sew
sistema_validate_spec   examples/packaging-machine/spec.json
sistema_create_project  examples/packaging-machine/spec.json
```
