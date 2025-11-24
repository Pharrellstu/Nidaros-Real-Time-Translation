FROM wowza/wowza-streaming-engine:latest-trial

USER root

# Copy JAR to temp location (won't interfere with initialization)
COPY subtitle-injector-1.0-SNAPSHOT.jar /tmp/

# Copy custom entrypoint
COPY entrypoint.sh /custom-entrypoint.sh
RUN chmod +x /custom-entrypoint.sh

# Use custom entrypoint
ENTRYPOINT ["/custom-entrypoint.sh"]