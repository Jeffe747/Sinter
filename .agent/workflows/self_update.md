---
description: Update the Linux Manager Agent to the latest version
---

1. To update the agent, simply send a POST request to the `/api/update` endpoint.
2. The agent will pull the latest code from the repository, rebuild itself, and restart the service.

   ```bash
   curl -X POST http://<SERVER_IP>:5000/api/update \
        -H "X-Agent-Key: <YOUR_KEY>"
   ```

3. **Verification**: Wait for a few seconds, then check the status to confirm the new version:

   ```bash
   curl http://<SERVER_IP>:5000/api/status \
        -H "X-Agent-Key: <YOUR_KEY>"
   ```
