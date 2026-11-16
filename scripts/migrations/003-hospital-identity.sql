-- Mizan hospital identity migration. Apply ONLY to an encrypted, verified staging copy
-- first. This migration never modifies staff accounts, invoices, or departments.
-- For existing databases, explicitly insert the approved hospital name after
-- applying this migration; NEVER guess a name or reset the original owner.
BEGIN;
CREATE TABLE IF NOT EXISTS "HospitalIdentities" (
    "Id" integer NOT NULL PRIMARY KEY,
    "Name" varchar(200) NOT NULL,
    CONSTRAINT "CK_HospitalIdentity_Singleton" CHECK ("Id" = 1),
    CONSTRAINT "CK_HospitalIdentity_NameNotBlank" CHECK (length(btrim("Name")) >= 3)
);
COMMIT;
