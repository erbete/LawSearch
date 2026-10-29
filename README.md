# LawSearch

Eksempel på tradisjonell RAG over norske lover fra Lovdata.

## Kom i gang

1. `docker compose up -d`
2. `dotnet user-secrets set Chat:ApiKey <nøkkel> --project src/Host`
3. Hent lovene og kopier dem du vil bruke til `data/`:
   ```
   curl -O https://api.lovdata.no/v1/publicData/get/gjeldende-lover.tar.bz2
   tar -xjf gjeldende-lover.tar.bz2
   ```
   Filnavnet er lovens dato og nummer, f.eks. `nl-20050617-062.xml` (arbeidsmiljøloven).
4. `dotnet run --project src/Host`

## Hva som brukes til hva

- **.NET 10:** selve appen, bygget som en modular monolith. `Ingestion` leser inn lovene, `Query` svarer på spørsmål, og `Host` kjører systemet.
- **Postgres + pgvector:** lagrer lovene, chunks og vektorer, og gjør vektorsøket og ordsøket.
- **Ollama:** kjører bge-m3, som gjør tekst om til vektorer, både ved innlesing og for hvert spørsmål.
- **Text Embeddings Inference (TEI):** kjører rerankeren bge-reranker-v2-m3.
- **Wolverine:** kjører innlesingen av lover til databasen ("ingestion") i bakgrunnen.
- **Språkmodell** (OpenAI-kompatibelt endepunkt): skriver om oppfølgingsspørsmål og lager svarene.

## Slik henger det sammen

```
Innlesing:  lovfil → paragrafer og ledd → chunks → vektorer → Postgres
Spørsmål:   spørsmål → søk → rerank → beste paragrafer → språkmodell → svar
```

### 1. Fra lovfil til database

Ved oppstart leses lovene i `data/`:

1. **Paragrafer og ledd:** hver lov deles opp i paragrafer, og hver paragraf i ledd.
2. **Chunks:** en chunk er en bit av en paragraf. Korte paragrafer blir én chunk; paragrafer over rundt 500 ord deles i flere.
3. **Vektorer:** hver chunk gjøres om til en vektor med bge-m3. En vektor er en liste med tall som beskriver hva teksten betyr, så tekster med lik mening får like tall.
4. **Lagring:** chunks, ledd og vektorer lagres i Postgres.

Eksempel på hvordan en lov ser ut etter stegene er å finne i `tests/Modules/Ingestion.Tests/TestData/Expected/`: `LawParser/` viser paragrafene og leddene, og `Chunker/` viser chunkene.

Bare nye og endrede lover leses inn, og lover uten fil fjernes. Søk virker underveis, men lover som ikke er ferdige, gir ingen treff.

Nullstille databasen: `docker compose down && docker volume rm lawsearch_pgdata && docker compose up -d`.

### 2. Fra spørsmål til paragrafer

1. **Omskriving:** er det et oppfølgingsspørsmål, skriver språkmodellen det om så det gir mening alene. «Hva om han ikke vil betale det tilbake?» blir f.eks. «Hva kan jeg gjøre hvis utleier ikke vil betale tilbake depositumet?».
2. **Søk:** databasen leter etter paragrafer som passer, på tre måter:
   - **På mening (vektorsøk):** finner tekst som betyr det samme som spørsmålet, selv om ordene er andre.
   - **På ord (ordsøk):** finner tekst som inneholder ordene i spørsmålet.
   - **På paragrafnummer (direkte oppslag):** skriver du «§ 28 a», hentes den paragrafen.

   Treffene slås sammen til paragrafer, og de beste (`Query:RerankCandidates`) går videre.
3. **Rerank:** rerankeren leser spørsmålet og hvert ledd sammen, og gir hvert ledd en poengsum for hvor godt det svarer. Paragrafen får poengsummen til sitt beste ledd, og de beste (`Query:TopK`) beholdes. Paragrafer nevnt i spørsmålet står alltid først.

   Svarer ikke rerankeren innen `Rerank:TimeoutSeconds`, brukes rekkefølgen fra søket. Rerank er altså valgfritt, systemet kjører fint uten, men det kan påvirke resultatet til søket.

### 3. Fra paragrafer til svar

1. Paragrafene, samtalen så langt og spørsmålet sendes til språkmodellen. Den skal bare svare ut fra paragrafene og henvise til lov, paragraf og ledd.
2. Henvisninger i svaret til paragrafer modellen ikke fikk, logges som advarsel.
3. Spørsmål og svar lagres, så samtalen kan fortsette.

## Spørsmål

- `http://localhost:5000/ask?q=…`: svar med kilder. Svaret har en `conversationId`.
- `http://localhost:5000/ask?q=…&c=<conversationId>`: oppfølgingsspørsmål i samme samtale. En ukjent id gir 404.

Eksempler ligger i `src/Host/.http`.

## Modeller

| | Tjeneste | Standard |
|---|---|---|
| Embedding (`Embedding`) | Ollama | bge-m3. Bytte krever ny innlesing |
| Reranker (`Rerank`) | TEI | bge-reranker-v2-m3 (ONNX) |
| Språkmodell (`Chat`) | OpenAI-kompatibelt endepunkt | |

`Chat:ReasoningEffort` (`low`, `high` eller `max`) bestemmer hvor mye språkmodellen tenker før den svarer. Mindre er raskere, men kan gi dårligere svar.

## Tester

- `dotnet test`: alle testene. Docker må kjøre, med lovene lest inn. Evalueringen stiller 17 spørsmål og tar rundt 7 minutter. Svarene lagres i `tests/Modules/Query.Tests/Evaluation/`.
- `dotnet test --project tests/Modules/Ingestion.Tests --explicit only`: skriver fasitfilene for parser og chunker på nytt. Sjekk med `git diff`.