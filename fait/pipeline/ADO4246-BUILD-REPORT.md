# Build Report: ADO#4246
## CloudFront Signed URLs for Office Online File Preview (PPTX/XLSX)

---

## Summary

Added an `ICloudFrontSignedUrlService` that generates short-lived CloudFront signed URLs from an RSA key pair loaded from AWS Secrets Manager. Updated `WorkspaceFileService` with `GetFilePreviewUrlAsync` — falls back to S3 presigned if CloudFront is not configured. Updated `ArtifactSidebarPanel.razor` to use CloudFront signed URL → Office Online inline iframe embed when configured, with graceful fallback to the existing "Open in Office Online" link for unconfigured environments. Added `.xlsx` to previewable extensions.

**Commit:** `2de97a86`  
**Build:** 0 errors

---

## CC Invocation

```bash
cat pipeline/ADO4246-build-brief.md | claude --model sonnet --print --dangerously-skip-permissions
```

---

## Files Modified

| File | Change |
|------|--------|
| `src/FortressAI.Web/FortressAI.Web.csproj` | Added `AWSSDK.CloudFront 3.7.*` and `AWSSDK.SecretsManager 3.7.*` package references |
| `src/FortressAI.Web/Services/ICloudFrontSignedUrlService.cs` | **NEW** — Interface with `IsConfigured` bool + `GetSignedUrlAsync(s3Key, expirySeconds?)` |
| `src/FortressAI.Web/Services/CloudFrontSignedUrlService.cs` | **NEW** — Singleton impl; loads RSA private key PEM from Secrets Manager on first call, caches in-memory, uses `AmazonCloudFrontUrlSigner.GetCannedSignedURL` for signing |
| `src/FortressAI.Web/Services/IWorkspaceFileService.cs` | Added `GetFilePreviewUrlAsync(s3Key, expirySeconds?, ct)` method |
| `src/FortressAI.Web/Services/WorkspaceFileService.cs` | Injected `ICloudFrontSignedUrlService`; implemented `GetFilePreviewUrlAsync` with CF/S3 fallback |
| `src/FortressAI.Web/Components/Chat/ArtifactSidebarPanel.razor` | Injected `ICloudFrontSignedUrlService`; Office file branch now generates CF signed URL → Office Online iframe embed; updated iframe `sandbox` to include `allow-popups-to-escape-sandbox`; added `.xls`, `.xlsx` to `PreviewableExtensions` |
| `src/FortressAI.Web/Program.cs` | Registered `IAmazonSecretsManager` singleton and `ICloudFrontSignedUrlService` (→ `CloudFrontSignedUrlService`) singleton |
| `src/FortressAI.Web/appsettings.json` | Added `"CloudFront"` section with empty placeholder keys |

---

## Infrastructure Changes Required (for Rhodey)

> All of these are one-time AWS setup steps. The app code is complete; it just needs the infrastructure provisioned and the env vars set.

### Step 1 — Create CloudFront Key Pair

```bash
# Generate RSA 2048 key pair
openssl genrsa -out cloudfront-signing-key.pem 2048
openssl rsa -pubout -in cloudfront-signing-key.pem -out cloudfront-signing-key-pub.pem
```

### Step 2 — Register Public Key in CloudFront

```bash
aws cloudfront create-public-key \
  --public-key-config '{
    "CallerReference": "fait-workspace-key-1",
    "Name": "fait-workspace-signing-key",
    "EncodedKey": "<contents of cloudfront-signing-key-pub.pem>",
    "Comment": "FAIT workspace file preview signing key"
  }'
# Note the returned Id (e.g. KXXXXXXXXXXXXX) — this is CloudFront:KeyPairId
```

### Step 3 — Create CloudFront Key Group

```bash
aws cloudfront create-key-group \
  --key-group-config '{
    "Name": "fait-workspace-key-group",
    "Items": ["<KeyPairId from Step 2>"]
  }'
# Note the returned Id — referenced in distribution config
```

### Step 4 — Create CloudFront OAC for S3

```bash
aws cloudfront create-origin-access-control \
  --origin-access-control-config '{
    "Name": "fait-workspace-oac",
    "Description": "OAC for fortress-user-workspaces bucket",
    "SigningProtocol": "sigv4",
    "SigningBehavior": "always",
    "OriginAccessControlOriginType": "s3"
  }'
# Note the returned Id — used in distribution
```

### Step 5 — Create CloudFront Distribution

```bash
aws cloudfront create-distribution --distribution-config '{
  "CallerReference": "fait-workspace-dist-1",
  "Comment": "FAIT workspace files for Office Online preview",
  "DefaultCacheBehavior": {
    "TargetOriginId": "fortress-user-workspaces-origin",
    "ViewerProtocolPolicy": "https-only",
    "TrustedKeyGroups": {
      "Enabled": true,
      "Quantity": 1,
      "Items": ["<KeyGroupId from Step 3>"]
    },
    "CachePolicyId": "4135ea2d-6df8-44a3-9df3-4b5a84be39ad",
    "AllowedMethods": {
      "Quantity": 2,
      "Items": ["GET", "HEAD"]
    }
  },
  "Origins": {
    "Quantity": 1,
    "Items": [{
      "Id": "fortress-user-workspaces-origin",
      "DomainName": "fortress-user-workspaces.s3.us-east-1.amazonaws.com",
      "S3OriginConfig": {"OriginAccessIdentity": ""},
      "OriginAccessControlId": "<OAC Id from Step 4>"
    }]
  },
  "Enabled": true,
  "HttpVersion": "http2"
}'
# Note the returned DomainName (e.g. d1234abcd.cloudfront.net) — this is CloudFront:DistributionDomain
```

### Step 6 — Update S3 Bucket Policy

Replace the `fortress-user-workspaces` bucket policy to allow only CloudFront OAC (remove any public access):

```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Sid": "AllowCloudFrontOAC",
      "Effect": "Allow",
      "Principal": {
        "Service": "cloudfront.amazonaws.com"
      },
      "Action": "s3:GetObject",
      "Resource": "arn:aws:s3:::fortress-user-workspaces/*",
      "Condition": {
        "StringEquals": {
          "AWS:SourceArn": "arn:aws:cloudfront::<ACCOUNT_ID>:distribution/<DISTRIBUTION_ID>"
        }
      }
    }
  ]
}
```

### Step 7 — Store Private Key in Secrets Manager

```bash
aws secretsmanager create-secret \
  --name "fait/cloudfront/workspace-signing-key" \
  --description "CloudFront RSA private key for FAIT workspace file preview" \
  --secret-string file://cloudfront-signing-key.pem \
  --region us-east-1
# Then delete cloudfront-signing-key.pem from local disk!
```

### IAM Permissions Required for `fortress-tools-deployer`

The ECS task role (not deployer) needs:
```json
{
  "Effect": "Allow",
  "Action": [
    "secretsmanager:GetSecretValue"
  ],
  "Resource": "arn:aws:secretsmanager:us-east-1:*:secret:fait/cloudfront/*"
}
```

For Rhodey's deployer role to set up:
```json
{
  "Effect": "Allow",
  "Action": [
    "cloudfront:CreateDistribution",
    "cloudfront:CreatePublicKey",
    "cloudfront:CreateKeyGroup",
    "cloudfront:CreateOriginAccessControl",
    "cloudfront:GetDistribution",
    "cloudfront:UpdateDistribution",
    "secretsmanager:CreateSecret",
    "secretsmanager:PutSecretValue",
    "s3:PutBucketPolicy",
    "s3:GetBucketPolicy"
  ],
  "Resource": "*"
}
```

---

## Configuration Required (ECS Environment Variables)

Set these as ECS task environment variables (or SSM Parameter Store entries) for the FAIT service:

| Variable | Value |
|----------|-------|
| `CloudFront__DistributionDomain` | `d1234abcd.cloudfront.net` (from Step 5) |
| `CloudFront__KeyPairId` | CloudFront Key Pair ID from Step 2 |
| `CloudFront__PrivateKeySecretName` | `fait/cloudfront/workspace-signing-key` |
| `CloudFront__UrlExpirySeconds` | `3600` (default — override to shorten/lengthen) |

**Note:** Until these are set, `IsConfigured = false` and the app falls back gracefully to the existing "Open in Office Online" link behavior. No regression.

---

## Self-Review Checklist

- [x] **AC 1:** CloudFront distribution configured — infra steps documented above (Steps 1–6)
- [x] **AC 2:** CF signed URLs generated instead of S3 presigned — `CloudFrontSignedUrlService.GetSignedUrlAsync` uses `AmazonCloudFrontUrlSigner.GetCannedSignedURL`
- [x] **AC 3:** PPTX preview flow — `SelectArtifact` extension check → `.pptx` in `PreviewableExtensions`, CF branch → `_previewUrl` set to Office Online embed URL → iframe renders
- [x] **AC 4:** XLSX preview flow — `.xlsx` added to `PreviewableExtensions` (was missing), same CF branch as PPTX
- [x] **AC 5:** PDF regression — PDF path unchanged (`else if ext == ".pdf"` → `_previewUrl = rawUrl` with S3 presigned, served directly by browser, no Office Online needed)
- [x] **AC 6:** Key pair stored in Secrets Manager — Step 7 above; `CloudFrontSignedUrlService` loads from `fait/cloudfront/workspace-signing-key`
- [x] **AC 7:** Expiry configurable via env var — `CloudFront__UrlExpirySeconds` env var, default 3600s
- [x] **No hardcoded secrets** — all config via `IConfiguration`; `PrivateKeySecretName` is the *name* of the secret, not the secret value
- [x] **IAM requirements documented** — see above for both ECS task role and deployer role

---

## ADO Comment

```
BUILD COMPLETE — ADO#4246 CloudFront signed URLs for Office Online file preview.
New: ICloudFrontSignedUrlService + CloudFrontSignedUrlService (PEM from Secrets Manager, cached, signed with AmazonCloudFrontUrlSigner).
Updated: WorkspaceFileService (GetFilePreviewUrlAsync with CF/S3 fallback), ArtifactSidebarPanel.razor (CF → Office Online inline embed; .xlsx added to previewable types).
Infrastructure setup steps (CloudFront dist, OAC, key group, S3 bucket policy, Secrets Manager) documented in BUILD REPORT.
Commit 2de97a86. Sending to Clint.
```

---

## Review Cycle 1 Fixes

**Commit:** `65699baa`  
**Build:** 0 errors

### Changes Made

#### C1 (Critical) — Removed `allow-same-origin` from iframe sandbox
- **File:** `src/FortressAI.Web/Components/Chat/ArtifactSidebarPanel.razor`
- Removed `allow-same-origin` from the iframe `sandbox` attribute.
- Final sandbox: `allow-scripts allow-popups allow-forms allow-popups-to-escape-sandbox`

#### I1 (Important) — Wired component through `WorkspaceFileService.GetFilePreviewUrlAsync` (Option A)
- **File:** `src/FortressAI.Web/Components/Chat/ArtifactSidebarPanel.razor`
- Removed `@inject ICloudFrontSignedUrlService CloudFrontSvc` injection — the component no longer knows about CloudFront directly.
- Office Online preview branch now calls `WorkspaceFileSvc.GetFilePreviewUrlAsync(artifact.S3Key, expirySeconds: 3600)`.
- Service returns CF signed URL when configured, S3 presigned URL as fallback.
- Component determines which path to take (inline embed vs fallback link) by checking if the returned URL contains `amazonaws.com` — if not, it's a CF URL and inline embed is used; otherwise the S3 presigned URL is stored in `_officeOnlineRawUrl` for the fallback "Open in Office Online" link.
- `IWorkspaceFileService` and `WorkspaceFileService` unchanged — `GetFilePreviewUrlAsync` was already correct.

#### Nitpicks — Not addressed (low priority per review)
- `volatile` on `_cachedPem` — deferred; not a correctness issue in practice (double-check lock is correct without volatile in .NET memory model for reference types on x64)
- Warning log for partial CloudFront config — deferred to future pass
