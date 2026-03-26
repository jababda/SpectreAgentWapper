"""
Shared GitHub Copilot API client used by all agents.

The GitHub Copilot API exposes an OpenAI-compatible chat-completions endpoint.
This module implements a minimal client using only the Python standard library
(urllib.request / json) so that no third-party packages are required inside the
container.
"""

import os
import urllib.request
import urllib.error
import json
from typing import Optional


COPILOT_API_BASE = os.environ.get(
    "COPILOT_API_BASE",
    "https://api.githubcopilot.com",
)


class CopilotClient:
    """Minimal OpenAI-compatible client for the GitHub Copilot API."""

    def __init__(self, token: str, model: str = "gpt-4o") -> None:
        if not token:
            raise ValueError("GITHUB_TOKEN must be set")
        self.token = token
        self.model = model
        self._base = COPILOT_API_BASE.rstrip("/")

    def chat(self, system: str, user: str, max_tokens: int = 8192) -> str:
        """
        Send a chat-completion request and return the assistant's message text.
        """
        payload = {
            "model": self.model,
            "messages": [
                {"role": "system", "content": system},
                {"role": "user", "content": user},
            ],
            "max_tokens": max_tokens,
        }

        url = f"{self._base}/chat/completions"
        data = json.dumps(payload).encode("utf-8")

        req = urllib.request.Request(
            url,
            data=data,
            method="POST",
            headers={
                "Authorization": f"Bearer {self.token}",
                "Content-Type": "application/json",
                "Accept": "application/json",
                "Copilot-Integration-Id": "copilot-agent",
                "Editor-Version": "copilot-agent/1.0",
            },
        )

        try:
            with urllib.request.urlopen(req, timeout=120) as resp:
                body = json.loads(resp.read().decode("utf-8"))
        except urllib.error.HTTPError as exc:
            detail = exc.read().decode("utf-8", errors="replace")
            raise RuntimeError(
                f"Copilot API error {exc.code}: {detail}"
            ) from exc

        try:
            return body["choices"][0]["message"]["content"]
        except (KeyError, IndexError) as exc:
            raise RuntimeError(f"Unexpected API response: {body}") from exc
