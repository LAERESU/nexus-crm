-- Baseline: tabel infrastruktur untuk Outbox pattern dan consumer idempoten.
-- Tabel bisnis dibuat oleh migrasi pada story masing-masing service.

CREATE TABLE OUTBOX_MESSAGES (
    ID              RAW(16)                   NOT NULL,
    TYPE            VARCHAR2(500)             NOT NULL,
    PAYLOAD         CLOB                      NOT NULL,
    OCCURRED_AT     TIMESTAMP WITH TIME ZONE  NOT NULL,
    PROCESSED_AT    TIMESTAMP WITH TIME ZONE,
    RETRY_COUNT     NUMBER(10)  DEFAULT 0     NOT NULL,
    ERROR           VARCHAR2(2000),
    CONSTRAINT PK_OUTBOX_MESSAGES PRIMARY KEY (ID)
);

-- Relay mengambil pesan yang belum diproses (PROCESSED_AT IS NULL) berurutan menurut OCCURRED_AT.
CREATE INDEX IX_OUTBOX_MESSAGES_PENDING ON OUTBOX_MESSAGES (PROCESSED_AT, OCCURRED_AT);

CREATE TABLE PROCESSED_EVENTS (
    EVENT_ID        RAW(16)                   NOT NULL,
    CONSUMER        VARCHAR2(200)             NOT NULL,
    PROCESSED_AT    TIMESTAMP WITH TIME ZONE  DEFAULT SYSTIMESTAMP NOT NULL,
    CONSTRAINT PK_PROCESSED_EVENTS PRIMARY KEY (EVENT_ID, CONSUMER)
);
