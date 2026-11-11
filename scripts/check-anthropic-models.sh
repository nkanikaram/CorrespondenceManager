# List available models (requires jq)
curl -s -H "x-api-key: $ANTHROPIC_API_KEY" https://api.anthropic.com/v1/models | jq .