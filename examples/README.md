# Esempi Grasshopper per Ant

Tre definizioni `.gh` con geometrie internalizzate, slider e istruzioni in italiano sul canvas. Non serve un file Rhino `.3dm` di supporto.

Richiedono **Rhino 8 per Windows, runtime .NET 8 e Ant 1.1.0** con le sue DLL. Gli esempi sono stati generati, salvati, riaperti e ricalcolati con Rhino 8.35 e la build locale di Ant 1.1.0. Se hai appena aggiornato il plugin, riavvia Rhino prima di aprirli. Le dimensioni e le tolleranze indicate sono nelle coordinate del modello; le aree sono in unità al quadrato.

| File | Cosa mostra | Anteprima del canvas |
| --- | --- | --- |
| [01 — Pannello forato](01_Pannello_forato_mesh_planare.gh) | Clean Geometry, Mesh Refinement, Planar Mesh, Clean and Merge Mesh, Mesh Topology | [PNG](01_Pannello_forato_mesh_planare.png) |
| [02 — Pulizia, qualità e parti](02_Pulizia_qualita_e_parti.gh) | Pulizia, controllo qualità, estrazione facce, separazione parti, normali, triangolazione e topologia | [PNG](02_Pulizia_qualita_e_parti.png) |
| [03 — Matching e selezione](03_Matching_e_selezione.gh) | Match Mesh Nodes, Match Mesh Faces, Dispatch di Grasshopper ed Extract Mesh Faces | [PNG](03_Matching_e_selezione.png) |

## 01 — Pannello forato, vincoli e refinement

Apri il file e premi **Run** sul componente Planar Mesh. Il toggle KR è salvato su `False`: dopo aver cambiato uno slider, premi di nuovo Run. Puoi attivare KR per il ricalcolo continuo.

Il pannello misura 12 × 8 e contiene un foro 2 × 2. Due linee interne e due punti coincidenti si trovano leggermente fuori piano. Clean Geometry li proietta sul pannello e rimuove il punto duplicato. Le tre uscite **S, E, R** sono collegate ai rispettivi ingressi del meshatore. Il refinement locale usa un punto con dimensione 0.25, rispetto alla dimensione globale 1.

Prove suggerite:

- Cambia **Size globale** tra 0.5 e 2 e confronta la densità.
- Cambia **Size locale** tra 0.1 e 1 e osserva la zona vicina al punto (3, 4).
- Riduci **PT** sotto 0.004 e leggi il log di Clean Geometry: alcuni input non saranno recuperabili con quella distanza di proiezione.
- Attiva la preview di Clean Geometry per distinguere geometria modificata, scartata e irrisolta.

Con i valori iniziali il test ha prodotto **232 facce**, conservando il foro aperto. Il numero può variare con versioni diverse del meshatore. Mesh Topology permette di ispezionare i bordi del pannello e del foro. Il pannello collegato a **I** di Planar Mesh mostra i tempi.

La tolleranza di Planar Mesh mantiene il significato storico del componente e non equivale a PT di Clean Geometry.

**Limite incontrato nelle prove:** con questa build, una variante con vincoli da bordo a bordo e attraverso il foro ha provocato l'errore nativo `Unknown OpenCASCADE point`. La definizione fornita usa vincoli interni, verificati. Il comportamento della variante resta da correggere nel percorso di embedding GMesh; non è stato modificato per creare questi esempi.

## 02 — Riparazione e gestione delle mesh

Il file ricalcola automaticamente. La lista iniziale contiene sette mesh: due pannelli con una fessura di 0.0005, una copia invertita di una faccia, un pannello separato, un triangolo allungato, un triangolo di area 0.00005 e una faccia degenere. Ci sono anche vertici inutilizzati.

Con **VT = 0.001**, **EL = 0** e **FA = 0.0001** il risultato contiene **4 facce in 3 parti**. Mesh Quality segnala il triangolo allungato; l'uscita F alimenta Extract Mesh Faces, che ne estrae una sola faccia. Mesh Parts separa le parti per connettività e le ordina per area. Normals uniforma le normali; Triangulate converte i quad in triangoli.

Prove suggerite:

- Porta **VT a 0**: la fessura resta e i due pannelli non formano più una parte unica.
- Porta **FA a 0** mantenendo VT a 0.001: il piccolo triangolo valido viene conservato.
- Esamina le uscite **S** e **R** di Extract Mesh Faces per separare difetti e resto della mesh.
- Per vedere una singola uscita, collegala a un parametro Mesh con preview attiva e spegni le altre preview.
- Esamina le mappe di Mesh Parts e Triangulate per risalire agli indici delle facce originali.

La preview iniziale di Clean and Merge Mesh include la diagnostica: arancione per le modifiche, rosso per gli scarti, viola per gli irrisolti. Le superfici di questo esempio sono aperte: non esiste un verso esterno univoco. Il parametro Flip di Normals permette di scegliere il verso dopo l'uniformazione.

## 03 — Ricerca di nodi e facce

Il file ricalcola automaticamente e usa una sola mesh con due isole. Quattro punti interrogano entrambi i matcher con **D = 0.20**.

| Punto | Posizione | Nodo trovato | Faccia trovata |
| --- | --- | --- | --- |
| 0 | Vicino al nodo (0, 0, 0) | Sì | Sì |
| 1 | Sopra l'interno di una faccia | No | Sì |
| 2 | Esattamente su un nodo della seconda isola | Sì | Sì |
| 3 | Lontano dalla mesh | No | No |

Match Mesh Nodes cerca i vertici; Match Mesh Faces cerca il punto più vicino sulla faccia, non il baricentro. Le liste restano allineate ai punti di ingresso: un mancato matching restituisce indice `-1` e **OK = False**.

**Dispatch** riceve gli indici F come lista e OK come pattern. L'uscita A invia solo gli indici validi a Extract Mesh Faces. I primi due punti raggiungono la stessa faccia, che viene estratta una sola volta: il risultato contiene **2 facce distinte**.

Prova a ridurre D fino a zero: rimane il solo punto esattamente sulla mesh. Con più mesh di ingresso usa anche l'uscita **M**, perché ogni indice di faccia o vertice è locale alla sua mesh; non collegare indistintamente indici provenienti da mesh diverse a un singolo Extract.

## Rigenerazione e controlli

I sorgenti sono in `tools/Examples`. Dalla radice di Ant, dopo la compilazione del plugin:

```powershell
dotnet run --project .\tools\Examples\Ant.Examples.csproj -c Release -- .\examples
```

Il comando rigenera i tre `.gh` e le anteprime `.png`, sovrascrivendo questi soli file di esempio. Usa un'altra cartella come ultimo argomento per conservare modifiche fatte alle definizioni. Richiede Rhino 8 installato e con licenza; avvia Rhino senza finestra e registra soltanto Ant e i componenti integrati necessari, senza caricare gli altri plugin dell'utente.

Il generatore verifica assenza di errori dei componenti, serializzazione e riapertura, collegamenti S/E/R, conservazione del foro, numero di facce/parti dopo la pulizia, selezione del difetto, triangolazione e risultati dei matcher. Le anteprime sono renderizzate dopo il ricalcolo; nel file 01 KR resta salvato su False.

Il precedente `Example/Test01.gh` e il relativo `.3dm` sono indipendenti da questa raccolta e non sono stati modificati.
