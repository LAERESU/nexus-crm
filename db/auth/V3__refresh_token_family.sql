-- NX-013: rotasi refresh token dan deteksi pemakaian ulang.
-- FAMILY_ID mengelompokkan semua token hasil rotasi dari satu login; bila token yang sudah dirotasi
-- dipakai lagi, seluruh keluarga dicabut. REVOKED_REASON menjelaskan alasan pencabutan.

ALTER TABLE REFRESH_TOKENS ADD (
    FAMILY_ID       RAW(16)        DEFAULT SYS_GUID() NOT NULL,
    REVOKED_REASON  VARCHAR2(30)
);

CREATE INDEX IX_REFRESH_TOKENS_FAMILY_ID ON REFRESH_TOKENS (FAMILY_ID);
