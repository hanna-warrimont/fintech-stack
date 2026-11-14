# Gleiche Version wie lokal (Aspire.Hosting.Keycloak 13.6.5). Realm mit Platzhaltern ${...}, die Keycloak beim Import
# aus Umgebungsvariablen füllt (Container-App-Secrets) – keine Geheimnisse im Image.
FROM quay.io/keycloak/keycloak:26.6.4 AS builder
ENV KC_DB=postgres KC_HEALTH_ENABLED=true
RUN /opt/keycloak/bin/kc.sh build

FROM quay.io/keycloak/keycloak:26.6.4
COPY --from=builder /opt/keycloak/ /opt/keycloak/
COPY portal-realm.json /opt/keycloak/data/import/portal-realm.json
ENTRYPOINT ["/opt/keycloak/bin/kc.sh"]
CMD ["start", "--optimized", "--import-realm"]
