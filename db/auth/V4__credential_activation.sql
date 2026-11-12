-- NX-021: undangan user. Kredensial dibuat tanpa password saat UserCreated diterima; password pertama diisi lewat
-- token aktivasi sekali pakai (berlaku 72 jam). Yang disimpan hanya hash SHA-256 token.

ALTER TABLE CREDENTIALS MODIFY (PASSWORD_HASH NULL);

ALTER TABLE CREDENTIALS ADD (
    ACTIVATION_TOKEN_HASH  VARCHAR2(64),
    ACTIVATION_EXPIRES_AT  TIMESTAMP WITH TIME ZONE
);

-- Baris tanpa token (NULL) tidak masuk index, jadi hanya token yang masih berlaku yang dijaga unik.
CREATE UNIQUE INDEX UX_CREDENTIALS_ACTIVATION_TOKEN ON CREDENTIALS (ACTIVATION_TOKEN_HASH);
