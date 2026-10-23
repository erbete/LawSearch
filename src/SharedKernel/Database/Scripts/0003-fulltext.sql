ALTER TABLE chunks ADD COLUMN tsv tsvector
    GENERATED ALWAYS AS (
        setweight(to_tsvector('norwegian', number || ' ' || replace(number, ' ', '') || ' ' || title), 'A') ||
        setweight(to_tsvector('norwegian', content), 'C')
    ) STORED;

CREATE INDEX chunks_tsv_idx ON chunks USING gin (tsv);