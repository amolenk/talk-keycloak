FROM maven:3.9.12-eclipse-temurin-21@sha256:c3c9d3ac4ce8431a3995c0318b8d390f448e693dd4fabc16e9b68d2e1f3d7b46 AS build
WORKDIR /build
COPY pom.xml .
RUN mvn -q dependency:go-offline
COPY src ./src
RUN mvn -q package

FROM scratch AS jar
COPY --from=build /build/target/keycloak-email-webhook-provider-1.0.0.jar /keycloak-email-webhook-provider.jar
