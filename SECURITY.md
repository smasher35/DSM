# Security Notice: API Key Management

## Overview

This application uses the OpenRouteService API for geocoding and route planning functionality. Access to this API requires an API key that must be kept confidential.

## Important Security Requirements

### 1. API Key Storage

- **NEVER** commit API keys to version control
- **NEVER** include API keys in source code files
- **NEVER** include API keys in configuration files that are tracked by Git
- **NEVER** include API keys in build artifacts or deployment packages

### 2. Proper Configuration

The application reads the API key from the system environment variable `DISIA_ORS_API_KEY`. This key must be:

- Configured on each deployment target individually
- Set as a machine-level environment variable (Windows: `setx /M`)
- Provisioned through secure secret management systems in production environments

### 3. Key Rotation and Revocation

If an API key is ever exposed (committed to version control, shared publicly, or otherwise compromised):

1. **Immediately revoke** the exposed key at https://openrouteservice.org
2. **Generate a new key** from the OpenRouteService dashboard
3. **Update the environment variable** on all affected systems
4. **Remove the exposed key** from Git history using tools like `git filter-branch` or `BFG Repo-Cleaner`
5. **Notify** all repository collaborators and artifact consumers

### 4. Files Protected by .gitignore

The following files are excluded from version control to prevent accidental credential exposure:

- `install_api.txt` - Contains the actual API key for local installation

### 5. Template Files

The repository includes template files that can be safely committed:

- `install_api.txt.template` - Template with placeholder for API key configuration

## For Developers

When setting up a development environment:

1. Copy `install_api.txt.template` to `install_api.txt`
2. Obtain your own API key from https://openrouteservice.org/dev/#/signup
3. Replace the placeholder in `install_api.txt` with your actual key
4. Run the command as Administrator to set the environment variable
5. **Never commit** your `install_api.txt` file

## For System Administrators

When deploying to production:

1. Use your organization's secret management system (Azure Key Vault, AWS Secrets Manager, HashiCorp Vault, etc.)
2. Provision the `DISIA_ORS_API_KEY` environment variable through your deployment pipeline
3. Ensure the key is not logged or exposed in deployment logs
4. Implement key rotation policies according to your security requirements

## Reporting Security Issues

If you discover a security vulnerability or exposed credential, please report it immediately to the repository maintainers through a private channel (not through public issues).
