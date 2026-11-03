/** Shared cache key for `GET /api/auth/v1/profile`. AuthSync clears the cache on login/logout. */
export const OWN_PROFILE_QUERY_KEY = ["own-profile"] as const;
