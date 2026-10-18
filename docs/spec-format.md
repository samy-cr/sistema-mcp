# Project specification format

A SISTEMA project is described as JSON and passed to `sistema_validate_spec` / `sistema_create_project`
(as text or as path to a `.json` file). Keys are case-insensitive, comments are allowed.

**Rule for assistants:** never invent safety-relevant values (PLr, category, MTTFd/B10D, DC, CCF, category
requirements). Leave them out – the validator turns every gap into a question. Ask the user all questions at
once, fill in the answers, validate again until `complete` is `true`.

## Structure

```
project            name*, machine, author, tester, manager, documentation, number, version, status
defaults           missionTimeYears (default 20), operatingProfile
safetyFunctions[]  id, name*, plr* (a–e) or riskGraph {severity S1|S2, frequency F1|F2, possibility P1|P2},
                   plrJustification, trigger*, reaction*, safeState*, operatingMode, demandRate, responseTime,
                   documentation, subsystems[]*
```

### Subsystem – three variants

1. **From a library** (device with PL/PFH, e.g. safety relay, light curtain, safety PLC):
   `{ "name": "...", "library": { "componentId": "<id from library_search>", "useCase": 0 } }`
2. **Directly from a data sheet**:
   `{ "name": "...", "direct": { "pl": "e", "pfh": 1.2e-9, "category": "4", "manufacturer": "...", "partNumber": "...", "source": "safety manual rev. 3" } }`
3. **Built from components** (designated architecture):

```
name*, role (input|logic|output), category* (B|1|2|3|4), categoryRequirementsMet* (true after confirming),
categoryJustification, ccfMeasures* (categories 2–4, keys from sistema_list_measures, ≥ 65 points),
ccfJustification, missionTimeYears, operatingProfile, description,
channels: { ch1: [component…]*, ch2: [component…] (categories 3, 4), test: [component…] (category 2) }
```

### Component (one block with one element in SISTEMA)

```
name*, technology* (electromechanical|electronic|pneumatic|hydraulic|mechanical|other),
reliability – one of:
   mttfd (years)
   b10d (operations) + nop (operations/year) or operatingProfile
   library { componentId, useCase }   (VDMA DeviceType 2/3: MTTFd, λd, MTTF, MTBF, B10D, B10)
dc (0…0.99) and/or dcMeasure (key from sistema_list_measures), dcJustification,
mttfdJustification, description, manufacturer, partNumber
```

### Operating profile (for B10D → MTTFd = B10D / (0.1 · nop))

`{ "daysPerYear": 220, "hoursPerDay": 16, "cycleTimeSeconds": 900 }` – or `operationsPerHour` instead of the
cycle time. Can be set in `defaults`, per subsystem or per component; the most specific one wins.

## Example

```json
{
  "project": { "name": "Assembly cell MZ-100", "machine": "MZ-100", "author": "J. Doe" },
  "defaults": { "missionTimeYears": 20, "operatingProfile": { "daysPerYear": 220, "hoursPerDay": 16, "operationsPerHour": 4 } },
  "safetyFunctions": [
    {
      "id": "SF-01",
      "name": "Emergency stop (stop category 0)",
      "plr": "d",
      "trigger": "Emergency stop button S1 pressed",
      "reaction": "Safety relay K10 de-energises contactors Q1/Q2",
      "safeState": "Drives without energy",
      "subsystems": [
        {
          "name": "Input: emergency stop S1", "role": "input", "category": "3", "categoryRequirementsMet": true,
          "ccfMeasures": ["separation", "diversity", "overvoltage", "emc", "environment"],
          "channels": {
            "ch1": [ { "name": "S1 NC contact 1", "technology": "electromechanical", "b10d": 100000, "nop": 220, "dcMeasure": "in-cross-full" } ],
            "ch2": [ { "name": "S1 NC contact 2", "technology": "electromechanical", "b10d": 100000, "nop": 220, "dcMeasure": "in-cross-full" } ]
          }
        },
        { "name": "Logic: safety relay K10", "role": "logic", "direct": { "pl": "e", "pfh": 1.2e-9, "category": "4" } },
        {
          "name": "Output: contactors Q1/Q2", "role": "output", "category": "3", "categoryRequirementsMet": true,
          "ccfMeasures": ["separation", "overvoltage", "emc", "environment", "well-tried", "fmea"],
          "channels": {
            "ch1": [ { "name": "Contactor Q1", "technology": "electromechanical", "b10d": 1300000, "dcMeasure": "out-direct" } ],
            "ch2": [ { "name": "Contactor Q2", "technology": "electromechanical", "b10d": 1300000, "dcMeasure": "out-direct" } ]
          }
        }
      ]
    }
  ]
}
```

## Workflow

1. `library_search` for the devices (first `library_list_manufacturers` → `library_download` or, for web portals,
   download in the browser → `library_import`).
2. `sistema_validate_spec` → ask the user the `questions`, resolve `errors`, check `warnings`.
3. `sistema_create_project` → `.ssm` file (never overwrites without `overwrite: true`).
4. `sistema_open_in_sistema` → the user checks the messages in SISTEMA and saves.
5. `sistema_read_project` → official PL/PFH calculated by SISTEMA.

The estimate returned by the validator uses the simplified procedure of ISO 13849-1 and is conservative.
