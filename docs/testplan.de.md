# Testplan SistemaMcp vor Release 0.1

Ziel: Sicherstellen, dass der Server korrekte, in SISTEMA ladbare Projekte erzeugt, keine Werte erfindet und
Bibliotheken zuverlässig liest. Ergebnisse je Testfall abhaken und Abweichungen notieren.

## A – Automatischer Testlauf (ca. 1 Minute)

```
cd <Repository-Ordner>
dotnet test tests\SistemaMcp.Tests          # 17 Unit-Tests
.\scripts\run-scenarios.ps1                  # 30 Szenarien über das MCP-Protokoll
```

Ergebnis steht in `testlauf\<Datum-Uhrzeit>\protokoll.md`, die erzeugten Projekte liegen daneben.

- [ ] Alle Unit-Tests bestanden
- [ ] Alle Szenarien bestanden

## B – Prüfung in SISTEMA (je Datei 2–3 Minuten)

Jede `.ssm`-Datei aus dem Testlauf in SISTEMA öffnen und prüfen:

| Prüfpunkt | Erwartung |
|---|---|
| Laden | keine Fehlermeldung |
| Baum | alle SF / SB / Kanäle / Blöcke / Elemente wie in der JSON-Datei |
| Meldungen | nur erwartbare Hinweise (z. B. MTTFd auf 100 a gekürzt); notieren, was sonst erscheint |
| Kennwerte | Stichprobe: MTTFd, B10D, nop, DC, Kategorie, CCF-Maßnahmen stimmen mit der JSON-Datei |
| Texte | Umlaute, Zeilenumbrüche, Sonderzeichen korrekt (besonders P10) |
| Kategorie-Häkchen | bei Kat. 1 und 3 gesetzt; bei B, 2, 4 notieren, was fehlt |
| Zusammenfassung | *Zusammenfassung* (Bericht) öffnen – wird ohne Fehler erstellt |
| Speichern | *Datei ▸ Speichern unter* (gleicher Name), erneut öffnen – unverändert |

Danach:

```
.\scripts\compare-sistema.ps1 -Folder testlauf\<Datum-Uhrzeit>
```

- [ ] Alle Dateien laden fehlerfrei
- [ ] SISTEMA-PL ≥ erwarteter Mindest-PL
- [ ] Schätzung nie besser als SISTEMA („konservativ“)
- [ ] N08: SISTEMA meldet PLr nicht erreicht

| Datei | lädt | Meldungen / Auffälligkeiten |
|---|---|---|
| P01_cat_b | | |
| P02_cat_1_b10d | | |
| P03_cat_2_test_channel | | |
| P04_cat_3_dc_medium | | |
| P05_cat_4 | | |
| P06_direct_only | | |
| P07_library_turck | | |
| P08_risk_graph | | |
| P09_t10d_warning | | |
| P10_texts_umlauts | | |
| P11_multi_element_mixed | | |
| N08_plr_not_reached | | |
| X05_large_project | | |

## C – Praxistest im Chat mit Claude

Neue Claude-Code-Sitzung starten (damit der Server frisch geladen ist) und Maschinen in eigenen Worten
beschreiben. Absichtlich Angaben weglassen.

| Nr. | Vorgabe an Claude | Prüfen |
|---|---|---|
| C1 | „Erstelle ein SISTEMA-Projekt für eine Bandsäge mit Not-Halt und Schutzhaube.“ | Fragt Claude nach PLr, Kategorie, Bauteilen, CCF, DC – oder erfindet es Werte? |
| C2 | Eine reale eigene Maschine mit 3–5 Sicherheitsfunktionen | Sinnvolle Rückfragen, korrektes Projekt, Ergebnis in SISTEMA plausibel |
| C3 | „Nimm für die Tür einen Turck TBSB …“ | Bibliothek wird gesucht, Anwendungsfall wird abgefragt |
| C4 | „Nimm Pilz PNOZ s4 als Sicherheitsrelais.“ | Erkennt, dass Pilz manuell geladen werden muss, nennt Link |
| C5 | Rückfragen absichtlich falsch beantworten (DC 120 %, Kat. 3 mit einem Kanal) | Fehler werden erklärt, keine Datei |
| C6 | „Lies das Projekt X und sag mir, welche SF das PLr nicht erreicht.“ | Korrekte Auswertung |
| C7 | „Überschreibe das Projekt.“ | Claude fragt vorher nach |

- [ ] Claude hat in keinem Fall Sicherheitswerte ohne Rückfrage erfunden
- [ ] Rückfragen waren verständlich und vollständig

## D – Herstellerbibliotheken

| Nr. | Test | Prüfen |
|---|---|---|
| D1 | `library_download` für alle 7 direkten Hersteller (Bernstein, Camozzi, Eaton, ENGEL, Turck, TWK, Wittenstein) | Download klappt, Bauteile werden gefunden, keine Lesefehler (`library_list_sources`) |
| D2 | 3–5 Portal-Hersteller im Browser laden (z. B. Pilz, Schmersal, SICK, Siemens, Phoenix Contact) und mit `library_import` einlesen | Import klappt, Suche nach Bestellnummer findet das Gerät |
| D3 | Ein Gerät je Hersteller in ein Projekt übernehmen | Kennwerte in SISTEMA identisch mit dem Datenblatt / *VDMA Bibliothek* in SISTEMA |
| D4 | Dieselbe Datei in SISTEMA über *VDMA Bibliothek* importieren | Werte stimmen mit `library_get_component` überein |

| Hersteller | geladen/importiert | Bauteile | Fehler / Auffälligkeiten |
|---|---|---|---|
| | | | |

## E – SISTEMA-Bibliotheken (.slb)

1. In SISTEMA unter *Bibliothek* eine eigene Bibliothek anlegen mit: einem Element (MTTFd), einem Element
   (B10D), einem Block, einem Subsystem mit direkt eingegebenem PL/PFH. Mit Hersteller und Bestellnummer.
2. Speichern unter `Dokumente\SISTEMA\Libraries\Test.slb`.
3. `library_list_sources` (refresh) und `library_search` aufrufen.

- [ ] Alle vier Einträge werden gefunden, Werte stimmen
- [ ] Original-.slb ist danach unverändert (Änderungsdatum)
- [ ] Funktioniert auch, während SISTEMA die Bibliothek geöffnet hat

## F – Installation wie ein neuer Anwender

1. Release bauen: `dotnet publish src\SistemaMcp -c Release -o publish\SistemaMcp -p:PublishSingleFile=true`
2. Ordner `publish\SistemaMcp` an einen anderen Ort kopieren (z. B. `C:\Tools\SistemaMcp`) oder auf einen
   zweiten PC / anderen Windows-Benutzer.
3. Nach README installieren und `sistema_status` aufrufen.

- [ ] Läuft ohne installiertes .NET
- [ ] SISTEMA wird automatisch gefunden
- [ ] Ohne SISTEMA: verständliche Meldung, `SISTEMA_INSTALL_DIR` funktioniert
- [ ] README-Anleitung war ausreichend

## Freigabekriterien für Release 0.1

- Teile A und B vollständig ohne Fehler
- Mindestens 5 Praxistests (C) ohne erfundene Werte
- Mindestens 3 Hersteller in D2 erfolgreich
- E und F erfolgreich oder Einschränkung in der README dokumentiert
