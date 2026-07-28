import assert from "node:assert/strict";
import {
  adminPermissions,
  canAccessAdmin,
  hasPermission,
} from "../src/integrations/financial-copilot/admin-permissions.ts";
import type { AuthUser } from "../src/integrations/financial-copilot/auth.ts";

const user = (permissions: string[]): AuthUser => ({
  userId: "user",
  email: "admin@example.com",
  tenantId: "tenant",
  roles: [],
  permissions,
});

assert.equal(canAccessAdmin(null), false);
assert.equal(canAccessAdmin(user([])), false);
assert.equal(canAccessAdmin(user([adminPermissions.usersRead])), true);
assert.equal(canAccessAdmin(user([adminPermissions.usersManage])), false);
assert.equal(
  hasPermission(user([adminPermissions.creditsAdjust]), adminPermissions.creditsAdjust),
  true,
);
assert.equal(
  hasPermission(user([adminPermissions.creditsAdjust]), adminPermissions.plansManage),
  false,
);

console.log("Admin permission checks passed.");
