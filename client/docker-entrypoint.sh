#!/bin/sh
set -e

# Default to / if BASE_PATH is not set
BASE_PATH=${BASE_PATH:-/}

# Remove trailing slash for nginx location matching (except for root)
if [ "$BASE_PATH" != "/" ]; then
    BASE_PATH_TRIMMED=$(echo "$BASE_PATH" | sed 's:/$::')
else
    BASE_PATH_TRIMMED="/"
fi

# Origin of the API, for the connect-src directive of the Content-Security-Policy. Leave unset
# when the API is served from the same origin as this app — 'self' already covers that. Set it
# (scheme and host only, e.g. https://api.example.gov.uk) when the API is on a different host,
# or the browser blocks every request the app makes.
API_ORIGIN=${API_ORIGIN:-}

# Replace the BASE_PATH and API_ORIGIN placeholders in nginx config
sed -e "s|BASE_PATH|${BASE_PATH_TRIMMED}|g" -e "s|API_ORIGIN|${API_ORIGIN}|g" /etc/nginx/conf.d/default.conf.template > /etc/nginx/conf.d/default.conf

echo "Configured nginx with BASE_PATH: ${BASE_PATH_TRIMMED}"
echo "Configured Content-Security-Policy connect-src with API_ORIGIN: ${API_ORIGIN:-(same origin)}"
cat /etc/nginx/conf.d/default.conf

# Execute the CMD
exec "$@"
