# Beispiel: Verpackungsmaschine

[English](README.md) · [Spezifikation](spec.json) · [Anleitung](../../docs/guide.de.md)

Dieses Beispiel beruht auf einer realen Testsitzung mit SistemaMcp (anonymisiert, auf drei Sicherheitsfunktionen
gekürzt). Ausgangspunkt waren nur eine Stückliste und die Vorgabe „PLr d laut Risikobeurteilung“. Es zeigt, wie das
Gespräch abläuft, welche Rückfragen kommen, welche Annahmen getroffen werden – und was man an der Anlage und an der
eigenen Arbeitsweise verbessern kann.

## 1. Ausgangslage

| Bestellnummer | Anzahl | Gerät |
|---|---|---|
| AZM201Z-I2-ST2-T-1P2PW | 7 | Schmersal AZM201Z (Schutztürüberwachung) |
| BDF100-NH-G-ST | 5 | Schmersal Befehlsgerät mit Not-Halt |
| RSS260-D-LSTM12-8-0,25M | 1 | Schmersal RFID-Sicherheitssensor |
| SFB-PN-IRT-8M12-IOP-V2 | 2 | Schmersal Safety-Fieldbox (PROFINET/PROFIsafe) |
| 6ES7517-3FP00-0AB0 | 1 | Siemens F-CPU 1517F-3 PN/DP |
| MDA90A-0640-503-X-S00 | 1 | SEW MOVIDRIVE modular Einzelachse (groß) |
| MDD90A-0040-503-X-S00 | 1 | SEW MOVIDRIVE modular Doppelachse (klein) |
| CSS21A | 1 | SEW Sicherheitskarte MOVISAFE (PROFIsafe) |

Erste Nachricht an den Assistenten:

> Projekt Verpackung. PLr d anhand der Risikobeurteilung. Erstelle anhand der Stückliste selbständig
> Sicherheitsfunktionen. *(Stückliste)* SEW wird nur über die Safety-Karte über PROFIsafe realisiert.

## 2. Bisherige Arbeitsweise (manuell in SISTEMA)

1. Sicherheitsfunktionen aus Stückliste und Schaltplan ableiten.
2. Für jeden Hersteller die VDMA-Bibliothek suchen, laden und in SISTEMA importieren.
3. In SISTEMA SF, Subsysteme, Kanäle, Blöcke und Elemente von Hand anlegen.
4. Je Gerät den richtigen Anwendungsfall aus der Bibliothek wählen.
5. Kategorie, CCF-Maßnahmen, DC, Schaltspiele, Kategorie-Häkchen je Subsystem eintragen.
6. Gleiche Ketten für jede SF wiederholen (hier dreimal Feldbox, F-CPU, SEW).
7. Ergebnis prüfen, Bericht erzeugen.

Fehlerquellen: vergessene Häkchen, falscher Anwendungsfall, uneinheitliche Werte zwischen gleichartigen SF, fehlende
Begründungen, T10D wird übersehen.

## 3. Ablauf mit SistemaMcp

### Runde 1 – Entwurf aus der Stückliste

Der Assistent ordnet die Bestellnummern zu und schlägt Sicherheitsfunktionen vor:

| SF | Funktion | Kette |
|---|---|---|
| SF-01 | Not-Halt | BDF100 → SFB → F-CPU → CSS21A → Achsen |
| SF-02 | Schutztür – Stillsetzen bei geöffneter Tür | AZM201Z → SFB → F-CPU → CSS21A → Achsen |
| SF-03 | Tür/Klappe RSS260 – Stillsetzen bei Öffnen | RSS260 → SFB → F-CPU → CSS21A → Achsen |

Der Server meldet, was fehlt – Reaktion, sicherer Zustand und PL/PFH/Kategorie jedes Geräts – und der Assistent fragt
zusätzlich: STO oder SS1? Repräsentative oder einzelne SF für die gleich verschalteten Türen und Not-Halte?

### Bibliotheken

| Hersteller | Weg | Ergebnis |
|---|---|---|
| Schmersal | automatisch: Dateilink auf der Produktseite gefunden, `library_download` | components V9, 144 Geräte |
| Siemens | Portal blockt Skripte → Nutzer lädt Datei, `library_import` | 1.22.0, 982 Geräte |
| SEW | Portal blockt Skripte → Nutzer lädt Datei, `library_import` | 2022-12, 144 Geräte |

Alle Geräte werden direkt über die Bestellnummer der Stückliste gefunden (z. B. `AZM201Z-I2-ST2-T-1P2PW` →
„AZM 201“).

### Runde 2 – Antworten

> BDF100 an SFB, dort 2-kanalige Auswertung, Kat. 3. CCF passt. 1× pro Stunde betätigt. CSS21A: STO bei der kleinen
> Achse, SS1 bei der großen Achse. AZM 201 bestätigt.

Der Assistent setzt um und **benennt seine Annahmen**:

- CCF: Trennung, Überspannungsschutz, bewährte Bauteile, EMV, Umwelt = 70 Punkte (keine Diversität, zwei gleiche
  Öffner).
- DC Not-Halt **90 %** (Kreuzvergleich mit Testpulsen) statt 99 % – 99 % nur, wenn Querschlüsse sicher erkannt werden.
- Betriebszeit **24/7** (8 760 Betätigungen/Jahr) → **T10D 11,4 Jahre**: Not-Halt-Geräte nach 11 Jahren tauschen.

### Runde 3 – Verdrahtung der Antriebe

> Große Achse mit Geber. Kleine Achse über CSS21A, über PROFINET.

→ CSS21A „mit Geber“ einmal in der Kette (sie steuert beide Achsen), dazu MDA und MDD mit STO 1-kanalig (ungünstigere
Annahme, PFH identisch).

### Ergebnis

`sistema_validate_spec`: vollständig, keine Fehler, Warnung T10D. Projekt erzeugt und in SISTEMA geöffnet.

| SF | PLr | Schätzung Server | PFH (Summe) |
|---|---|---|---|
| SF-01 Not-Halt | d | c (vorsichtig, siehe unten) | – |
| SF-02 Schutztür | d | d | 1,45E-8 |
| SF-03 RSS260 | d | d | 1,33E-8 |

SF-01: Das Not-Halt-Subsystem besteht aus Bauteilen und hat in der Schätzung keine PFH; mit vier PL-d-Subsystemen wird
dann eine Stufe abgezogen. SISTEMA rechnet die PFH exakt – maßgeblich ist das Ergebnis dort.

## 4. Optimierungsvorschläge für die Anlage

Vorschläge zur Prüfung durch den Konstrukteur, keine Bewertung der Anlage:

| Punkt | Heute | Vorschlag | Wirkung |
|---|---|---|---|
| STO-Ansteuerung | 1-kanalig angenommen | 2-kanalig bestätigen | Reserve bis PL e |
| DC Not-Halt | 90 % angenommen | Querschlusserkennung der SFB bestätigen → 99 % | höherer DCavg |
| Betriebsprofil | 24/7 angenommen | reale Tage/Stunden angeben | realistisches T10D, Wartungsintervall |
| Not-Halt-Austausch | – | T10D in Betriebsanleitung/Wartungsplan | Pflichtangabe bei T10D < Gebrauchsdauer |
| Repräsentative SF | 7 Türen / 5 Not-Halte als je eine SF | dokumentieren, dass alle gleich verschaltet sind und auf beide Achsen wirken | nachvollziehbare Validierung |
| Achsen | beide Achsen in jeder SF | wirkt eine Tür nur auf eine Achse, eigene SF je Achse | weniger konservativ, klarer |

## 5. Optimierungen der Arbeitsweise

- **Bibliotheken vorab bereitstellen** (ein Ordner, `SISTEMA_MCP_LIBRARY_PATHS`) – spart eine Runde und macht die
  Version nachvollziehbar.
- **Verdrahtungskonzept mitschicken:** 1-/2-kanalig, Geber, wer schaltet wen. Das waren hier die meisten Rückfragen.
- **Betriebsprofil angeben:** Tage, Stunden, Taktzahlen je Tür/Not-Halt.
- **Stoppkategorie je Antrieb nennen.**
- Mit diesen Angaben wäre das Projekt in **einer** Runde vollständig gewesen.

## 6. Nachstellen

```
library_download  <ZIP-Link von der Schmersal-Produktseite „SISTEMA VDMA components“>
library_import    <Ordner der Siemens-Bibliothek>   id = siemens
library_import    <Ordner der SEW-Bibliothek>       id = sew
sistema_validate_spec   examples/packaging-machine/spec.json
sistema_create_project  examples/packaging-machine/spec.json
```
