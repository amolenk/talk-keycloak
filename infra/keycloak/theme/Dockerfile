FROM node:22.22.1-bookworm-slim@sha256:4f77a690f2f8946ab16fe1e791a3ac0667ae1c3575c3e4d0d4589e9ed5bfaf3d AS node

FROM maven:3.9.12-eclipse-temurin-21@sha256:c3c9d3ac4ce8431a3995c0318b8d390f448e693dd4fabc16e9b68d2e1f3d7b46 AS theme
COPY --from=node /usr/local/ /usr/local/
WORKDIR /theme
# The postinstall hook (keycloakify sync-extensions) needs the Vite config and src/
# to place the extension-managed email templates next to the owned ones.
COPY package.json package-lock.json index.html tsconfig.json tsconfig.node.json vite.config.ts ./
COPY src/ ./src/
RUN npm ci
RUN npm run build-keycloak-theme

FROM scratch AS jar
COPY --from=theme /theme/dist_keycloak/city-picks.jar /city-picks.jar
