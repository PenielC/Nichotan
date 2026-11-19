# Nichotan web app: builds the SPA, serves it with nginx, and forwards /api to the API.
# Build context is the frontend/ folder:  docker build -t nichotan-web frontend
FROM node:22-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:1.29-alpine AS runtime
# nginx renders /etc/nginx/templates/*.template with the container's environment variables at start-up.
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
COPY security-headers.conf /etc/nginx/snippets/security-headers.conf
COPY --from=build /app/dist /usr/share/nginx/html
# API_HOST: the API's host name (no scheme), e.g. nichotan-api.internal.example.azurecontainerapps.io
ENV API_HOST=localhost
ENV API_SCHEME=https
ENV NGINX_ENVSUBST_FILTER=API_
EXPOSE 8080
