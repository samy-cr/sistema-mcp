# SistemaMcp – MCP-Server für IFA SISTEMA

[English](README.md) · [Anleitung](docs/guide.de.md) · [Beispielprojekt](examples/packaging-machine/README.de.md) · [Spezifikationsformat](docs/spec-format.md)

SistemaMcp verbindet einen KI-Assistenten (Claude oder einen anderen [MCP](https://modelcontextprotocol.io)-Client)
mit [IFA SISTEMA](https://www.dguv.de/ifa/praxishilfen/praxishilfen-maschinenschutz/software-sistema/index.jsp), dem
kostenlosen Werkzeug zur Bewertung sicherheitsbezogener Steuerungsteile nach **DIN EN ISO 13849-1**.

Sie beschreiben die Maschine in eigenen Worten – Stückliste, Risikobeurteilung, Verdrahtungskonzept – und der
Assistent baut daraus ein vollständiges SISTEMA-Projekt: Sicherheitsfunktionen, Subsysteme, Kanäle und Bauteile mit
Kennwerten aus den **VDMA-66413-Bibliotheken** der Hersteller. Was fehlt, wird **gefragt, nie geraten**. Das
Ergebnis öffnet sich in SISTEMA; dort findet die maßgebliche Berechnung statt.

> **Hinweis.** Keine Verbindung zum IFA oder zur DGUV. SistemaMcp ersetzt nicht die Beurteilung durch eine
> fachkundige Person. Jedes erzeugte Projekt muss in SISTEMA geprüft werden. Es werden keine Daten des IFA oder von
> Herstellern mit der Software verteilt.

---

## Was der Server kann

| | |
|---|---|
| **Projekte erzeugen** | aus einer JSON-Spezifikation, die der Assistent für Sie schreibt: Sicherheitsfunktionen, PLr (direkt oder per Risikograph), Subsysteme mit Kategorie, Kanäle, CCF, DC, MTTFd/B10D, Gebrauchsdauer |
| **Fragen statt raten** | Die Prüfung macht aus jeder Lücke eine konkrete Rückfrage – PLr, Kategorie, CCF-Maßnahmen, Diagnosedeckungsgrad, Schalthäufigkeit, welcher Anwendungsfall eines Geräts gilt … |
| **Plausibilität prüfen** | MTTFd je Kanal (Symmetrisierung, Kappung), DCavg, CCF-Punkte, T10D, konservative PL-Schätzung, Widersprüche wie PFH ≠ PL |
| **Herstellerdaten nutzen** | liest VDMA-66413-Bibliotheken (XML/ZIP) und SISTEMA-Bibliotheken (`.slb`), sucht nach Typ oder Bestellnummer, übernimmt PL/PFH, MTTFd, B10D mit Quellenangabe |
| **Rundlauf mit SISTEMA** | öffnet die Datei in SISTEMA und liest die dort berechneten PL/PFH zurück |
| **Bestehende Projekte lesen** | vollständiger Baum jeder `.ssm`-Datei, z. B. für Reviews oder „Welche SF erreicht ihr PLr nicht?“ |

## So funktioniert es

```
 Sie (Chat)                      KI-Assistent                       SistemaMcp                    SISTEMA
 ──────────                      ────────────                       ──────────                    ───────
 Stückliste, Risiko-    ───►  schreibt JSON-Spezifikation  ───►  sistema_validate_spec
 beurteilung, Verdrahtung                                          │  Rückfragen / Fehler / Schätzung
                         ◄───  stellt alle offenen Fragen   ◄──────┘
 Antworten              ───►  vervollständigt Spezifikation ───►  library_search / _import (VDMA, SLB)
                                                            ───►  sistema_create_project ──► .ssm-Datei
                                                            ───►  sistema_open_in_sistema ─────────────► berechnet PL/PFH
 prüfen, „Speichern unter“                                                                           │
                         ◄───  meldet amtliche Ergebnisse   ◄──  sistema_read_project ◄─────────────┘
```

## Empfohlene Arbeitsweise

1. **Bibliotheken selbst bereitstellen.** Die meisten Hersteller (Siemens, SEW, SICK, Pilz, Phoenix Contact …)
   bieten ihre VDMA-66413-Dateien nur über Webportale an. Laden Sie die Bibliotheken Ihrer Vorzugskomponenten einmal
   herunter, legen Sie sie in einen Ordner und setzen Sie `SISTEMA_MCP_LIBRARY_PATHS` darauf (oder lassen Sie den
   Assistenten `library_import` ausführen). Jedes weitere Projekt geht dann schneller, ist reproduzierbar – und Sie
   bestimmen, welche Bibliotheksversion verwendet wird.
2. **Gute Eingaben liefern:** Stückliste, PLr je Gefährdung aus der Risikobeurteilung, Stoppkategorien,
   Verdrahtungskonzept (1- oder 2-kanalig, Geber, welches Gerät schaltet was) und Betriebsprofil (Tage, Stunden,
   Takte).
3. **Fragen beantworten, keine Lücken füllen lassen.** Schlägt der Assistent einen Wert vor, sagt er das – bestätigen
   oder korrigieren.
4. **In SISTEMA prüfen, mit *Datei ▸ Speichern unter* speichern,** dann die Ergebnisse zurücklesen lassen.

Das [Beispielprojekt](examples/packaging-machine/README.de.md) zeigt eine komplette Sitzung mit einer
Verpackungsmaschine (Schmersal, Siemens F-CPU, SEW MOVIDRIVE) – mit Rückfragen, Annahmen und
Optimierungsvorschlägen, und im Vergleich zur bisherigen manuellen Arbeitsweise.

## Voraussetzungen

- Windows mit installiertem **SISTEMA 3.0.x** (getestet mit 3.0.4, ISO 13849-1:2023)
- Ein MCP-Client, z. B. Claude Code oder Claude Desktop

Das Release ist eigenständig (keine .NET-Installation nötig) und läuft als 32-Bit-Prozess, weil SISTEMA eine
32-Bit-Firebird-Engine mitbringt, mit der `.slb`-Bibliotheken gelesen werden.

## Installation

1. `SistemaMcp-win-x86.zip` aus den [Releases](../../releases) laden und entpacken, z. B. nach `C:\Tools\SistemaMcp`.
2. Server eintragen:

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
         "env": { "SISTEMA_MCP_LIBRARY_PATHS": "D:\\Sicherheit\\VDMA-Bibliotheken" }
       }
     }
   }
   ```
3. Den Assistenten fragen: *„Prüfe den Status des SISTEMA-MCP.“*

| Umgebungsvariable | Bedeutung |
|---|---|
| `SISTEMA_MCP_LIBRARY_PATHS` | Ordner/Dateien mit VDMA-XML, entpackten ZIPs oder `.slb`, getrennt durch `;` – **empfohlen** |
| `SISTEMA_INSTALL_DIR` | SISTEMA-Ordner, falls nicht automatisch gefunden |
| `SISTEMA_MCP_DATA_DIR` | Ablage für geladene/importierte Bibliotheken (Standard `%LOCALAPPDATA%\SistemaMcp`) |

## Schnellstart

> *„Lege ein SISTEMA-Projekt ‚Palettierer PL-2‘ an. Not-Halt mit Schmersal BDF200 an einer Siemens F-CPU 1516F,
> Schutztür mit AZM300, Antriebe SEW MOVIDRIVE mit STO. Überall PLr d. Die Bibliotheken liegen in
> D:\Sicherheit\VDMA.“*

Der Assistent importiert die Bibliotheken, findet die Geräte, schlägt die Sicherheitsfunktionen vor und fragt, was
noch fehlt – etwa die Kategorie des Not-Halt-Eingangs, die CCF-Maßnahmen und wie oft er betätigt wird.

## Tools

| Tool | Zweck |
|---|---|
| `sistema_status` | Installation, Ordner, Bibliotheksstand, empfohlener Ablauf |
| `sistema_get_spec_format` | JSON-Format einer Projektspezifikation mit Beispiel |
| `sistema_list_measures` | Schlüssel für CCF-Maßnahmen (Anhang F), DC-Maßnahmen (Anhang E), Kategorie-Anforderungen, Risikograph |
| `sistema_validate_spec` | Rückfragen, Fehler, Warnungen und konservative Schätzung – schreibt nichts |
| `sistema_create_project` | schreibt die `.ssm`-Datei (lehnt Unvollständiges ab, überschreibt nie ungefragt) |
| `sistema_open_in_sistema` | öffnet eine Datei in SISTEMA |
| `sistema_read_project` | liest ein Projekt inkl. der von SISTEMA gespeicherten Ergebnisse |
| `sistema_list_projects` | listet `.ssm`-Dateien eines Ordners |
| `library_list_manufacturers` | Hersteller mit VDMA-66413-Bibliotheken (VDMA-Referenzliste) und deren Downloadseiten |
| `library_download` | lädt eine Bibliothek über einen direkten Dateilink in die lokale Ablage |
| `library_import` | importiert eine selbst geladene ZIP-/XML-/SLB-Datei oder einen Ordner |
| `library_search`, `library_get_component` | Geräte nach Typ, Bestellnummer, Text suchen; alle Anwendungsfälle anzeigen |
| `library_list_sources` | eingelesene Quellen und Lesefehler |

## Bauteilbibliotheken

Die [VDMA-Referenzliste](https://www.vdma.eu/66413bibo) nennt rund 40 Hersteller. Nur wenige bieten einen stabilen
direkten Dateilink (Bernstein, Camozzi, Eaton, ENGEL, Turck, TWK, Wittenstein; Schmersal über den Link auf der
Produktseite). Siemens, SEW, SICK, Pilz, Phoenix Contact, Festo und die meisten anderen setzen einen Browser voraus
(Suche, Login, Lizenzbestätigung). Getestete Importe: Schmersal components V9 (144 Geräte), Siemens 1.22.0 (982),
SEW 2022-12 (144), Turck 2024 (16). Bibliotheken bleiben auf Ihrem Rechner; bitte die Nutzungsbedingungen der
Hersteller beachten.

## Zur Schätzung

`sistema_validate_spec` nutzt das *vereinfachte Verfahren* der ISO 13849-1 und ist bewusst konservativ – z. B. hat ein
aus Bauteilen aufgebautes Subsystem in der Schätzung keine PFH, sodass SF mit mehreren PL-d-Subsystemen eine Stufe
niedriger geschätzt werden können, als SISTEMA rechnet. Die Schätzung dient dazu, Fehler früh zu finden; maßgeblich
ist das Ergebnis von SISTEMA.

## Grenzen (v0.1)

- Je Bauteil ein Block mit einem Element; bestehende Projekte werden nicht bearbeitet (nur Erzeugung).
- Kategorie-Häkchen werden nur für Kategorie 1 und 3 vorbelegt; bei B, 2, 4 in SISTEMA setzen.
- DC wird als Wert mit Begründung geschrieben, nicht als SISTEMA-Maßnahme.
- Der Risikograph wird ausgewertet und dokumentiert, das PLr steht als „direkt eingegeben“.
- `.slb`-Subsysteme werden übernommen, wenn ihr PL direkt eingegeben ist; EMI (ISO 13849-1:2023) und Software
  (SRESW/SRASW) werden nicht befüllt.
- SISTEMA speichert Ergebnisse nicht immer schon beim Öffnen – *Datei ▸ Speichern unter…* verwenden.

Siehe [Ausblick in der Anleitung](docs/guide.de.md#ausblick).

## Selbst bauen

```
dotnet test tests/SistemaMcp.Tests
dotnet publish src/SistemaMcp -c Release -o publish/SistemaMcp -p:PublishSingleFile=true
.\scripts\run-scenarios.ps1        # 30 End-to-End-Szenarien über das MCP-Protokoll
```

## Datenschutz

Der Server arbeitet lokal. Ins Netz geht er nur bei `library_download`. Keine Telemetrie.

## Lizenz

[MIT](LICENSE). SISTEMA ist ein Produkt des Instituts für Arbeitsschutz der Deutschen Gesetzlichen Unfallversicherung
(IFA). VDMA 66413 ist ein Einheitsblatt des VDMA e.V. Produktnamen gehören ihren jeweiligen Inhabern.
