# Anleitung SistemaMcp

[English version](guide.en.md) · [Zurück zur README](../README.de.md)

Diese Anleitung erklärt, wie SistemaMcp arbeitet, wie Sie es am besten einsetzen und wo die Grenzen liegen. Sie richtet
sich an Konstrukteure, Elektroplaner und Sicherheitsverantwortliche, die SISTEMA kennen.

## Inhalt

1. [Grundidee](#1-grundidee)
2. [Vom Gespräch zum SISTEMA-Projekt](#2-vom-gespräch-zum-sistema-projekt)
3. [Was der Assistent von Ihnen braucht](#3-was-der-assistent-von-ihnen-braucht)
4. [Bauteilbibliotheken](#4-bauteilbibliotheken)
5. [Wie die Struktur in SISTEMA abgebildet wird](#5-wie-die-struktur-in-sistema-abgebildet-wird)
6. [Modellierungsmuster aus der Praxis](#6-modellierungsmuster-aus-der-praxis)
7. [Rückfragen, Fehler, Warnungen, Annahmen](#7-rückfragen-fehler-warnungen-annahmen)
8. [Schätzung und Prüfung in SISTEMA](#8-schätzung-und-prüfung-in-sistema)
9. [Fehlerbehebung](#9-fehlerbehebung)
10. [Ausblick](#ausblick)

---

## 1. Grundidee

SISTEMA ist das Rechenwerkzeug; SistemaMcp ist die Brücke zwischen Ihrer Beschreibung der Maschine und einem
SISTEMA-Projekt. Der Server

- **erfindet keine Sicherheitswerte.** PLr, Kategorie, MTTFd/B10D, DC, CCF und die Bestätigung der
  Kategorie-Anforderungen kommen von Ihnen oder aus einer Herstellerbibliothek. Fehlt etwas, entsteht eine Rückfrage.
- **dokumentiert die Herkunft.** Bibliothek, Version, Hersteller und Anwendungsfall stehen im SISTEMA-Projekt; DC-Werte
  bekommen die Maßnahme nach Anhang E als Begründung.
- **überlässt die Rechnung SISTEMA.** Die eigene Schätzung dient nur dem frühen Erkennen von Fehlern.

Der Assistent (z. B. Claude) übersetzt Ihre Angaben in eine JSON-Spezifikation
([Format](spec-format.md)), ruft die Tools auf und stellt Ihnen die Rückfragen. Sie sehen die Spezifikation nicht,
wenn Sie nicht möchten – können sie aber jederzeit anfordern, speichern und wiederverwenden.

## 2. Vom Gespräch zum SISTEMA-Projekt

| Schritt | Was passiert | Tool |
|---|---|---|
| 1 | Sie beschreiben Maschine, Gefährdungen, Bauteile | – |
| 2 | Der Assistent sucht die Bauteile in den Bibliotheken | `library_search`, ggf. `library_download` / `library_import` |
| 3 | Er schreibt einen Entwurf der Sicherheitsfunktionen und lässt ihn prüfen | `sistema_validate_spec` |
| 4 | Er stellt Ihnen **alle** offenen Fragen gesammelt | – |
| 5 | Schritte 3–4 wiederholen sich, bis nichts mehr fehlt | `sistema_validate_spec` |
| 6 | Das Projekt wird geschrieben | `sistema_create_project` |
| 7 | SISTEMA öffnet sich; Sie prüfen und speichern mit *Datei ▸ Speichern unter* | `sistema_open_in_sistema` |
| 8 | Der Assistent liest die von SISTEMA berechneten PL/PFH und vergleicht mit dem PLr | `sistema_read_project` |

Typisch sind zwei bis drei Fragerunden. Je vollständiger die erste Beschreibung, desto weniger Runden.

## 3. Was der Assistent von Ihnen braucht

Je besser die Eingabe, desto weniger Rückfragen. Bewährt hat sich diese Liste:

| Angabe | Beispiel | Wozu |
|---|---|---|
| Projektname, Maschine, Autor | „Verpackung VM-3, Autor M. Muster“ | Projektkopf |
| **Stückliste** der sicherheitsrelevanten Teile | Bestellnummern, Stückzahlen | Bauteile finden, SF ableiten |
| **PLr je Sicherheitsfunktion** | „PLr d laut Risikobeurteilung“ oder S/F/P | Anforderung |
| **Stoppkategorie / Reaktion** | „große Achse SS1, kleine Achse STO“ | Reaktion, sicherer Zustand, Anwendungsfall |
| **Verdrahtungskonzept** | „Not-Halt 2-kanalig an Feldbox, STO 2-kanalig, Achse mit Sicherheitsgeber“ | Kategorie, Anwendungsfall, DC |
| **Diagnose** | „Querschlusserkennung durch Testpulse“, „Rückführkreis“ | DC |
| **CCF-Maßnahmen** | „Standardmaßnahmen erfüllt“ oder einzelne Maßnahmen | ≥ 65 Punkte (Kat. 2–4) |
| **Betriebsprofil** | „250 Tage, 2 Schichten à 8 h, Tür 4× pro Stunde“ | nop → MTTFd, T10D |
| Gebrauchsdauer | Standard 20 Jahre | Kennwerte |
| Gleichartige Funktionen | „alle 7 Türen gleich verschaltet“ | repräsentative SF |

Eine gute erste Nachricht kann so aussehen:

> Projekt „VM-3“, Autor M. Muster. PLr d für alle Funktionen. Stückliste siehe unten. Alle Sensoren hängen an zwei
> Schmersal-Feldboxen SFB (PROFIsafe) an einer F-CPU 1517F. Antriebe SEW MOVIDRIVE modular, Sicherheitskarte CSS21A,
> große Achse mit Sicherheitsgeber, SS1; kleine Achse STO. STO 2-kanalig. Not-Halt 2-kanalig, Testpulse mit
> Querschlusserkennung. CCF-Standardmaßnahmen erfüllt. Betrieb 250 Tage × 16 h, Türen etwa 4× pro Stunde, Not-Halt-Test
> 1× pro Woche. Gleich verschaltete Türen und Not-Halte bitte repräsentativ abbilden. Bibliotheken liegen in
> D:\Sicherheit\VDMA.

## 4. Bauteilbibliotheken

### 4.1 Empfehlung: Bibliotheken selbst bereitstellen

Automatisch laden lassen sich nur Hersteller mit einem festen Dateilink. Die meisten großen Hersteller verlangen einen
Browser (JavaScript-Portal, Login, Lizenzbestätigung) und sperren Skripte. Deshalb:

1. Legen Sie einen Ordner an, z. B. `D:\Sicherheit\VDMA`, mit einem Unterordner je Hersteller.
2. Laden Sie die Bibliotheken Ihrer Vorzugskomponenten herunter und entpacken Sie sie dort
   (`library_list_manufacturers` nennt die Downloadseiten).
3. Tragen Sie den Ordner als `SISTEMA_MCP_LIBRARY_PATHS` in die MCP-Konfiguration ein – oder lassen Sie ihn per
   `library_import` einmalig in die lokale Ablage kopieren.
4. Aktualisieren Sie die Bibliotheken bewusst (z. B. halbjährlich) und halten Sie fest, welche Version für ein Projekt
   gilt. Die Version steht auch im SISTEMA-Projekt.

Vorteile: reproduzierbare Ergebnisse, keine Abhängigkeit von Herstellerwebseiten, Sie entscheiden über die Version.

### 4.2 Was gelesen wird

| Format | Inhalt | Verwendung |
|---|---|---|
| VDMA 66413 XML (auch in ZIP) | Geräte mit Anwendungsfällen | Typ 1 (PL + PFH) und Typ 4 (nur PL) → Subsystem; Typ 2 (MTTFd, λ, MTTF/MTBF) und Typ 3 (B10D/B10) → Element |
| SISTEMA-Bibliothek `.slb` | Firebird-Datenbank | Subsysteme mit direkt eingegebenem PL/PFH, Blöcke, Elemente |

Ein Gerät hat oft **mehrere Anwendungsfälle** (z. B. Feldbox: Eingang 2-kanalig PL e / 1-kanalig PL d / Ausgang
1 Leitung / 2 Leitungen). Welcher gilt, hängt von Ihrer Verdrahtung ab – der Server fragt nach, wenn es nicht
eindeutig ist.

### 4.3 Suchen

`library_search` findet nach Typ, Bestellnummer, Name, Gruppe und Text – unabhängig von Leerzeichen und Bindestrichen.
Bestellnummern aus der Stückliste werden auf den Typ zurückgeführt: `AZM201Z-I2-ST2-T-1P2PW` findet „AZM 201“ (und
verwandte Typen wie „AZM 201 D“ – den passenden wählen Sie). Die Bestellnummer aus der Stückliste wird ins Projekt
übernommen, auch wenn die Bibliothek keine führt.

Widersprüche fallen auf: Ein PL, den die Kategorie nicht zulässt (B → höchstens b, 1 → c, 2 → d), ist bei eigenen
Angaben ein Fehler und bei Bibliothekswerten eine Warnung.

## 5. Wie die Struktur in SISTEMA abgebildet wird

| Spezifikation | SISTEMA | Bemerkung |
|---|---|---|
| `safetyFunctions[]` | SF | PLr direkt; Risikograph wird ausgewertet und als Text dokumentiert |
| Subsystem mit `library` (Typ 1/4) oder `direct` | SB, PL/PFH „direkt eingegeben“ | Hersteller, Bibliothek, Anwendungsfall werden übernommen |
| Subsystem mit `category` + `channels` | SB „aus Unterelementen“ | Kanal 1, Kanal 2, Testkanal |
| Bauteil in `ch1`/`ch2`/`test` | BL mit genau einem EL | MTTFd direkt oder B10D + nop; DC als Wert mit Begründung |
| `ccfMeasures` | CCF-Maßnahmen (CCFMID-001…008) | Punkte nach Anhang F |
| `categoryRequirementsMet` | Häkchen der Kategorie-Anforderungen | vorbelegt für Kat. 1 und 3 |

Die Datei wird so geschrieben, wie SISTEMA sie selbst schreibt (Feldliste von ssm 3.0.0, UTF-8, Datumsformat,
Zahlen mit Dezimalkomma). Berechnete Felder bleiben leer, SISTEMA füllt sie.

## 6. Modellierungsmuster aus der Praxis

**Gleichartige Funktionen repräsentativ abbilden.** Sieben gleich verschaltete Türen → eine SF „Schutztür“ mit dem
Hinweis „7 Stück, repräsentativ“. Werden Türen unterschiedlich ausgewertet oder wirken sie auf verschiedene Antriebe,
eigene SF anlegen.

**Sicherheitssteuerung als Kette.** Sensor → Feldbox/F-Peripherie → F-CPU → Antrieb: jedes Gerät mit PL/PFH ist ein
eigenes Subsystem. Die PFH wird summiert.

**Not-Halt aus Kontakten.** Not-Halt-Geräte liefern meist nur B10D der Öffner. Dann ist der Not-Halt ein Subsystem
„aus Bauteilen“: Kategorie (meist 3), zwei Öffner in Kanal 1 und 2, DC aus der Auswertung (Testpulse,
Querschlusserkennung), CCF, Betätigungshäufigkeit inkl. Prüfungen.

**Ein Sicherheitsmodul für mehrere Achsen.** Steuert eine Sicherheitskarte (z. B. SEW CSS21A) mehrere Achsen, steht die
Karte **einmal** in der Kette, gefolgt von den Leistungsteilen der Achsen (STO). So wird ihre PFH nicht doppelt
gezählt.

**Unsicherheit konservativ auflösen.** Ist etwas offen (DC-Wert, Betriebszeit, 1- oder 2-kanalig), nimmt der Assistent
die ungünstigere Variante und sagt das. Sie korrigieren später gezielt.

## 7. Rückfragen, Fehler, Warnungen, Annahmen

| Art | Bedeutung | Beispiel |
|---|---|---|
| **Rückfrage** | Information fehlt – Projekt wird nicht erzeugt | „Wie oft schaltet das Bauteil?“, „Welcher Anwendungsfall?“ |
| **Fehler** | Angabe widerspricht der Norm oder ist ungültig – Projekt wird nicht erzeugt | CCF 40 < 65, DC 150 %, Kategorie 3 ohne Kanal 2 |
| **Warnung** | Projekt wird erzeugt, aber es gibt etwas zu beachten | T10D < Gebrauchsdauer (Austausch), PFH passt nicht zum PL |
| **Annahme** | Normvorgabe wurde eingesetzt | Gebrauchsdauer 20 Jahre |

`sistema_list_measures` zeigt die zulässigen Schlüssel für CCF- und DC-Maßnahmen mit Punkten bzw. DC-Werten.

## 8. Schätzung und Prüfung in SISTEMA

Die Schätzung des Servers rechnet MTTFd je Kanal (gekappt, symmetrisiert), DCavg, CCF und ermittelt den PL nach dem
vereinfachten Verfahren. Für Subsysteme aus Bauteilen hat sie keine PFH; enthält eine SF mehrere PL-d-Subsysteme,
kombiniert sie über die PL-Stufen und liegt dann oft eine Stufe unter SISTEMA. Das ist gewollt vorsichtig.

**Maßgeblich ist SISTEMA:**
1. Datei öffnen (`sistema_open_in_sistema`), Meldungen lesen.
2. Kategorie-Häkchen, Bibliotheksdaten, Texte stichprobenartig prüfen.
3. *Datei ▸ Speichern unter* (gleicher Name) – erst dann stehen die Ergebnisse in der Datei.
4. `sistema_read_project` liefert PL/PFH je SF und ob das PLr erreicht ist.

## 9. Fehlerbehebung

| Problem | Lösung |
|---|---|
| `sistema_status`: SISTEMA nicht gefunden | `SISTEMA_INSTALL_DIR` setzen |
| SLB: „needs the 32-bit server build“ | Release-exe verwenden (win-x86) |
| `library_download`: „manual-download-required“ | Datei im Browser laden, `library_import` |
| Gerät nicht gefunden | nach Typ-Stamm suchen; `library_list_sources` zeigt Lesefehler |
| SISTEMA: „Datei konnte nicht geöffnet werden“ | Meldung an das Projekt (Issue) mit Spezifikation; Datei nicht von Hand ändern |
| Ergebnisse fehlen nach dem Öffnen | in SISTEMA *Speichern unter* verwenden |

## Ausblick

Geplant bzw. aus den Praxistests abgeleitet:

- Geräte einmal definieren und in mehreren SF verwenden (weniger doppelte Rückfragen)
- PFH-Näherung für Subsysteme aus Bauteilen in der Schätzung
- automatisches Finden von Dateilinks auf Herstellerseiten
- Kategorie-Häkchen für B, 2 und 4; DC als SISTEMA-Maßnahme
- bestehende Projekte ergänzen und ändern
- EMI und Software-Anforderungen (ISO 13849-1:2023)

Rückmeldungen und Beiträge sind willkommen (Issues / Pull Requests).
