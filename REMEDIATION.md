# Remediation Steps for Exposed OpenRouteService API Key

## Immediate Actions Required

### 1. Revoke the Exposed API Key

The following API key was exposed in the repository and must be revoked immediately:

```
eyJvcmciOiI1YjNjZTM1OTc4NTExMTAwMDFjZjYyNDgiLCJpZCI6IjAzY2ZiZWM5ODMxMjRlNmQ4ZDhmZWRlMDgzNzY3OWMxIiwiaCI6Im11cm11cjY0In0=
```

**Action:** Log in to https://openrouteservice.org and revoke this key immediately.

### 2. Generate a New API Key

After revoking the exposed key:

1. Generate a new API key from the OpenRouteService dashboard
2. Do NOT commit this new key to the repository
3. Configure it as an environment variable on each system that needs it

### 3. Clean Git History

The exposed key exists in the Git history and must be removed:

**Option A: Using BFG Repo-Cleaner (Recommended)**
```bash
# Download BFG from https://rtyley.github.io/bfg-repo-cleaner/
java -jar bfg.jar --replace-text passwords.txt
git reflog expire --expire=now --all
git gc --prune=now --aggressive
git push --force
```

Where `passwords.txt` contains:
```
eyJvcmciOiI1YjNjZTM1OTc4NTExMTAwMDFjZjYyNDgiLCJpZCI6IjAzY2ZiZWM5ODMxMjRlNmQ4ZDhmZWRlMDgzNzY3OWMxIiwiaCI6Im11cm11cjY0In0=
```

**Option B: Using git filter-branch**
```bash
git filter-branch --force --index-filter \
  "git rm --cached --ignore-unmatch install_api.txt" \
  --prune-empty --tag-name-filter cat -- --all
git reflog expire --expire=now --all
git gc --prune=now --aggressive
git push --force
```

### 4. Update All Deployed Systems

For each system where the application is deployed:

1. Remove the old environment variable:
   ```cmd
   reg delete "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment" /v DISIA_ORS_API_KEY /f
   ```

2. Set the new API key:
   ```cmd
   setx DISIA_ORS_API_KEY "NEW_KEY_HERE" /M
   ```

3. Restart the application

### 5. Notify Stakeholders

Inform all repository collaborators and system administrators about:

- The credential exposure incident
- The need to pull the cleaned repository history
- The requirement to update their local environment variables
- The new security procedures documented in SECURITY.md

## Changes Made in This Patch

1. **install_api.txt**: Replaced the real API key with a placeholder and added security warnings
2. **.gitignore**: Added `install_api.txt` to prevent future commits of real credentials
3. **install_api.txt.template**: Created a safe template file that can be committed
4. **ConfiguracaoRotas.cs**: Enhanced documentation with security warnings
5. **README.md**: Added comprehensive section on API key configuration and security
6. **SECURITY.md**: Created detailed security documentation for credential management

## Verification

After applying this patch and cleaning the Git history:

```bash
# Verify the exposed key is not in the current working tree
git grep "eyJvcmciOiI1YjNjZTM1OTc4NTExMTAwMDFjZjYyNDgiLCJpZCI6IjAzY2ZiZWM5ODMxMjRlNmQ4ZDhmZWRlMDgzNzY3OWMxIiwiaCI6Im11cm11cjY0In0="

# Verify the exposed key is not in the Git history
git log -S "eyJvcmciOiI1YjNjZTM1OTc4NTExMTAwMDFjZjYyNDgiLCJpZCI6IjAzY2ZiZWM5ODMxMjRlNmQ4ZDhmZWRlMDgzNzY3OWMxIiwiaCI6Im11cm11cjY0In0=" --all

# Verify install_api.txt is ignored
git check-ignore install_api.txt
```

All commands should return no results (or confirm the file is ignored).

## Prevention

Going forward:

1. All developers must read SECURITY.md before contributing
2. Pre-commit hooks should be configured to scan for potential secrets
3. Regular security audits should be performed on the repository
4. Consider using tools like git-secrets or truffleHog for automated scanning
