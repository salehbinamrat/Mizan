-- Apply to a staging copy first. Take a verified backup before touching production.
-- Existing unassigned desks remain historical only; do not merge their transactions.
BEGIN;
ALTER TABLE "Departments" ADD COLUMN IF NOT EXISTS "MaxActiveUsers" integer NOT NULL DEFAULT 20;
ALTER TABLE "Departments" DROP CONSTRAINT IF EXISTS "CK_Department_UserLimit";
ALTER TABLE "Departments" ADD CONSTRAINT "CK_Department_UserLimit"
  CHECK ("MaxActiveUsers" BETWEEN 1 AND 10000);
ALTER TABLE "CashDesks" ADD COLUMN IF NOT EXISTS "OwnerUserId" uuid NULL;
ALTER TABLE "CashDesks" DROP CONSTRAINT IF EXISTS "FK_CashDesks_StaffUsers_OwnerUserId";
ALTER TABLE "CashDesks" ADD CONSTRAINT "FK_CashDesks_StaffUsers_OwnerUserId"
  FOREIGN KEY ("OwnerUserId") REFERENCES "StaffUsers"("Id") ON DELETE RESTRICT;
CREATE UNIQUE INDEX IF NOT EXISTS "IX_CashDesks_OwnerUserId" ON "CashDesks"("OwnerUserId")
  WHERE "OwnerUserId" IS NOT NULL AND "IsActive" = TRUE;
COMMIT;
