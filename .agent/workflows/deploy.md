---
description: Deploy a .NET app using the Linux Manager Agent
---

1. Ensure the Linux Agent is running on the target server.
2. Ask the user for the following information if not already provided:
   - **Target Server IP**
   - **API Key** (Check `./client_secret` if available, otherwise ask user)
   - **Repository URL**
   - **App Name**
   - **Branch** (default: `main`)
   - **Git Token** (if repository is private)

3. Construct the deployment payload and execute the request:

   **Dry Run:** To verify build without deploying:
   ```bash
   curl -N -H "X-Agent-Key: <YOUR_KEY>" \
        -H "Content-Type: application/json" \
        -X POST http://<SERVER_IP>:5000/api/deploy \
        -d '{
              "repoUrl": "<REPO_URL>",
              "appName": "<APP_NAME>",
              "branch": "<BRANCH>",
              "token": "<TOKEN>",
              "dryRun": true
            }'
   ```
   
   **Deploy:** To deploy the application:
   ```bash
   # Example command pattern
   curl -N -H "X-Agent-Key: <YOUR_KEY>" \
        -H "Content-Type: application/json" \
        -X POST http://<SERVER_IP>:5000/api/deploy \
        -d '{
              "repoUrl": "<REPO_URL>",
              "appName": "<APP_NAME>",
              "branch": "<BRANCH>",
              "token": "<TOKEN>"
            }'
   ```

   **Monitoring Progress:** The `curl -N` command streams the output (e.g., `[INFO]`, `[SUCCESS]`, `[FAIL]`).
   - Parse this output to inform the user of the progress.
   - Wait for `[SUCCESS]` or `[FAIL]` to determine the outcome.

4. **(Optional) Open Firewall Port:**
   If the application requires a specific port (e.g., 8080), ask the user if they want to open it, then run:

   ```bash
   curl -H "X-Agent-Key: <YOUR_KEY>" \
        -H "Content-Type: application/json" \
        -X POST http://<SERVER_IP>:5000/api/system/open-port \
        -d '{ "port": <PORT>, "protocol": "tcp" }'
   ```
