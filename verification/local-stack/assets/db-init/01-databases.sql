-- Fixed local-demo passwords requested by the presenter.
CREATE USER keycloak WITH PASSWORD 'demo';
CREATE DATABASE keycloak OWNER keycloak;
CREATE USER citypicks WITH PASSWORD 'demo';
CREATE DATABASE citypicks OWNER citypicks;
