# Build context: folder web/. Contoh: docker build -t nexus/web:dev web
FROM node:20-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.30-alpine
# Terapkan perbaikan keamanan paket OS yang belum masuk ke base image (mis. CVE-2026-77214 di libexpat),
# lalu kembali ke user non-root bawaan image (uid 101).
USER root
RUN apk upgrade --no-cache
USER 101
# Template diproses envsubst saat start → /etc/nginx/conf.d/default.conf
COPY templates/default.conf.template /etc/nginx/templates/default.conf.template
ENV API_UPSTREAM=http://gateway:8080
COPY --from=build /app/dist /usr/share/nginx/html
# nginx-unprivileged sudah berjalan sebagai user non-root (uid 101)
EXPOSE 8080
