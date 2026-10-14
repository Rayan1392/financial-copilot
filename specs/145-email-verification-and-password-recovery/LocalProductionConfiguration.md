# Feature 145 — Local and production email configuration

## Configuration files

- src/backend/FinancialCopilot.API/appsettings.json contains shared email defaults and non-secret Gmail SMTP values (host, port, TLS mode, username, and sender identity). Email remains disabled by default, and no SMTP password is present. The Development-local overlay enables local delivery and supplies the secret.
- src/backend/FinancialCopilot.API/appsettings.Development.json is already tracked and contains existing Development settings. Keep it intact; do not put SMTP secrets in it.
- src/backend/FinancialCopilot.API/appsettings.Development.Local.json is the local-only overlay. It is ignored by Git, excluded from API output and publish content, and loaded only when ASPNETCORE_ENVIRONMENT=Development. It contains the operator-approved Gmail host, port, username, app password, StartTls mode, sender identity, and detected Vite frontend URL (`http://localhost:5173`). Development email delivery is enabled. The SMTP password and token hash secret are not reproduced in this document.
- .env.example has production placeholders for manual copying to the VPS .env. The populated .env is ignored and must stay out of Git. This change does not edit the populated .env or contact the production server.

The API loads settings in this order: shared appsettings.json, tracked appsettings.Development.json, Development User Secrets, the optional local overlay, environment variables, then command-line values. The overlay is inserted before environment-variable sources, so environment variables and command-line settings still take precedence. Production does not load the local overlay.

## SMTP option binding

The API binds Authentication:Email to AuthEmailOptions and Authentication:Smtp to SmtpEmailOptions. Docker Compose's api service maps each production .env variable to the matching .NET environment key:

| .env variable | .NET configuration key |
|---|---|
| AUTH_EMAIL_ENABLED | Authentication__Email__Enabled |
| AUTH_EMAIL_FRONTEND_BASE_URL | Authentication__Email__FrontendBaseUrl |
| AUTH_EMAIL_TOKEN_HASH_SECRET | Authentication__Email__TokenHashSecret |
| AUTH_EMAIL_VERIFICATION_TOKEN_HOURS | Authentication__Email__VerificationTokenHours |
| AUTH_EMAIL_PASSWORD_RESET_TOKEN_MINUTES | Authentication__Email__PasswordResetTokenMinutes |
| AUTH_EMAIL_COOLDOWN_SECONDS | Authentication__Email__EmailCooldownSeconds |
| AUTH_EMAIL_MAXIMUM_REQUESTS_PER_HOUR | Authentication__Email__EmailMaximumRequestsPerHour |
| AUTH_SMTP_HOST / AUTH_SMTP_PORT | Authentication__Smtp__Host / Authentication__Smtp__Port |
| AUTH_SMTP_USERNAME / AUTH_SMTP_PASSWORD | Authentication__Smtp__UserName / Authentication__Smtp__Password |
| AUTH_SMTP_SECURITY | Authentication__Smtp__Security |
| AUTH_SMTP_SENDER_ADDRESS / AUTH_SMTP_SENDER_NAME | Authentication__Smtp__SenderAddress / Authentication__Smtp__SenderName |
| AUTH_SMTP_TIMEOUT_SECONDS | Authentication__Smtp__TimeoutSeconds |

Compose uses empty/default values when email is disabled. When enabling production delivery, set every required email and SMTP value in the ignored VPS .env; the API validates the configured frontend URL, token secret, SMTP host, port, TLS mode, sender, timeout, and paired SMTP credentials at startup.

## Manual production transfer

1. Review .env.example and copy the Feature 145 variable block to the intended VPS .env using the approved secure operations channel.
2. Replace all angle-bracket placeholders with production values. Generate a unique token-hash secret of at least 32 characters and use the verified HTTPS frontend origin.
3. Set AUTH_EMAIL_ENABLED=true only after SMTP credentials, sender authorization, and DNS mail policy have been provisioned.
4. Keep the filled VPS .env private and excluded from unapproved backups or support bundles.
5. Run docker compose config -q on the VPS after populating its .env; restart only under the normal approved release process. This task does not deploy or change the server.

No SMTP connection or email send is part of configuration validation.

## Validation record

Validation performed: the .NET configuration harness confirmed Development environment selection, loaded appsettings.Development.json and the local overlay, let environment variables override local JSON, and bound both AuthEmailOptions and SmtpEmailOptions. It confirmed Production does not load the local overlay and binds the synthetic environment variables. The local overlay was then populated with the approved Gmail settings; non-secret fields and password presence were checked without printing the password. Docker Compose parsed successfully and synthetic, non-credential sentinel values reached every SMTP and email .NET key on the Production api service. The backend Release build and focused OwnedIdentityEndpointTests passed (16/16). API publish succeeded and its output did not contain the local overlay. Git confirmed the overlay is ignored and the existing tracked appsettings.Development.json remains tracked.

The local SMTP settings are ready for local testing, but no email was sent. Production `.env` values remain placeholders for manual transfer; the production server and credentials were not modified. No deployment was performed.
