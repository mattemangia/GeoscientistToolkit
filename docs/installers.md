# Installer multi-piattaforma

L'ecosistema di installazione è composto da due progetti:

1. **InstallerPackager** (`net8.0` console) comprime i build `dotnet publish`, copia la cartella `ONNX/` e il server `NodeEndpoint` nelle sottocartelle `app/` e `node-endpoint/`, genera gli script `start-endpoint.*` e aggiorna automaticamente `docs/installer-manifest.json` con URL, dimensioni e hash SHA256 degli archivi.
2. **InstallerWizard** (wizard TUI basato su [Terminal.Gui](https://github.com/gui-cs/Terminal.Gui)) scarica/legge gli archivi compressi, permette di scegliere runtime, componenti (app principale o endpoint) e shortcut desktop, supporta l'elevazione UAC su Windows e gestisce gli aggiornamenti automatici leggendo il manifest.

## Prerequisiti

- .NET 8 SDK per eseguire il packager e per compilare i binari self-contained.
- Modelli `.onnx` salvati dentro `ONNX/` (verranno copiati automaticamente nella cartella `ONNX` accanto all'eseguibile pubblicato).
- Eventuale feed HTTP/HTTPS o spazio CDN dove pubblicare `docs/installer-manifest.json` e gli archivi prodotti.

Il wizard di installazione **non** richiede il .NET SDK sulle macchine target perché consuma gli archivi già compilati.

## Configurazione dei manifest

1. Aggiornare `docs/installer-manifest.json` con il numero di versione (`version`), la descrizione dei prerequisiti e i componenti (`components`) da visualizzare nel wizard. Ogni componente deve indicare `relativePath` (cartella all'interno dell'archivio) e `targetSubdirectory` (cartella finale sotto la directory di installazione).
2. Personalizzare `InstallerWizard/installer-settings.json` per definire il nome prodotto, il percorso di default e l'URL pubblico del manifest.
3. Personalizzare `InstallerPackager/packager-settings.json` per indicare dove salvare gli archivi (`PackagesOutputDirectory`), il prefisso degli URL (`PackageBaseUrl`), i percorsi dei progetti (`ProjectPath` e `NodeProjectPath`) e l'eventuale nome dell'eseguibile del server (`NodeExecutableName`).

## Creazione dei pacchetti

```bash
# Da /workspace/GeoscientistToolkit
cd InstallerPackager
# facoltativo: verificare/aggiornare packager-settings.json
cat packager-settings.json
# genera tutti gli archivi e aggiorna docs/installer-manifest.json
dotnet run --project InstallerPackager.csproj
```

Il packager, per ogni runtime elencato nel manifest:

1. esegue `dotnet publish` per `GeoscientistToolkit` e `NodeEndpoint` creando bundle self-contained;
2. copia la cartella `ONNX/` dentro `app/ONNX/` così da distribuire anche i modelli;
3. crea gli script `start-endpoint.cmd`, `start-endpoint.sh`, `start-endpoint.command` dentro `node-endpoint/` puntando agli eseguibili pubblicati;
4. comprime l'intera struttura (`app/` + `node-endpoint/`) in `artifacts/installers/<NomePacchetto>-<rid>.zip`;
5. calcola SHA256 e dimensione e aggiorna `packageUrl`, `sha256`, `sizeBytes` nel manifest.

Pubblicare i file `.zip` nella posizione indicata da `PackageBaseUrl` e distribuire il manifest aggiornato.

## Utilizzo dell'InstallerWizard

1. Compilare il wizard per le piattaforme desiderate (ad es. `dotnet publish InstallerWizard/InstallerWizard.csproj -c Release -r win-x64 --self-contained true`).
2. Copiare l'eseguibile risultante insieme al `installer-settings.json` preconfigurato.
3. All'avvio l'utente potrà:
   - scegliere il runtime disponibile per quell'archivio;
   - selezionare se installare l'app principale, il server endpoint o entrambi;
   - decidere se creare i collegamenti desktop;
   - elevare i privilegi UAC su Windows quando necessario per scrivere in `Program Files` o creare scorciatoie.
4. Durante l'installazione gli archivi vengono scaricati/verificati (hash SHA256), decompressi e copiati nelle cartelle finali. Il wizard genera script di avvio e, se richiesto, i collegamenti desktop (`.lnk`, `.desktop`, `.command`).

## Aggiornamenti automatici

- In `plan.InstallPath` viene salvato `install-info.json` con versione, runtime, componenti e URL del manifest.
- Ad ogni avvio del wizard viene scaricato il manifest configurato e confrontato il campo `version` con quello installato.
- Se il server fornisce un numero di versione maggiore, il wizard propone l'aggiornamento e scarica nuovamente gli archivi corrispondenti.
- Per distribuire un aggiornamento è sufficiente rieseguire `InstallerPackager` (per rigenerare archivi/hash) e pubblicare il manifest aggiornato.

## Suggerimenti

- Inserire i modelli `.onnx` richiesti in `ONNX/` prima di lanciare il packager per evitare installazioni incomplete.
- È possibile mantenere più copie di `packager-settings.json` per ambienti diversi (es. staging e production) e sostituire il file prima di lanciare il packager.
- Nel manifest si possono aggiungere prerequisiti specifici della piattaforma (driver GPU, runtime aggiuntivi). Verranno mostrati nella schermata iniziale del wizard.
