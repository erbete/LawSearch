CREATE TABLE IF NOT EXISTS documents (
    id          text PRIMARY KEY,
    title       text NOT NULL,
    file_name   text NOT NULL UNIQUE,
    file_hash   text NOT NULL,
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS chunks (
    id           text PRIMARY KEY,
    document_id  text NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    paragraph_id text NOT NULL,
    chapter      text NOT NULL,
    number       text NOT NULL,
    title        text NOT NULL,
    subsections  jsonb NOT NULL,
    content      text NOT NULL,
    embedding    vector($EmbeddingDimensions$) NOT NULL -- set in appsettings.json at Embedding:Dimensions
);

CREATE INDEX IF NOT EXISTS chunks_embedding_idx
    ON chunks USING hnsw (embedding vector_cosine_ops);

CREATE INDEX IF NOT EXISTS chunks_paragraph_idx
    ON chunks (paragraph_id);

CREATE TABLE IF NOT EXISTS conversation_history (
    id                 bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    conversation_id    uuid NOT NULL,
    question           text NOT NULL,
    rewritten_question text,
    answer             text NOT NULL,
    created_at         timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS conversation_history_conversation_idx
    ON conversation_history (conversation_id, id);