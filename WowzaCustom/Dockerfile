FROM wowza/wowza-streaming-engine:latest-trial

# Switch to root to install packages
USER root

# Install Java development tools (we'll need this to compile the module)
RUN apt-get update && \
    apt-get install -y openjdk-11-jdk maven && \
    rm -rf /var/lib/apt/lists/*

# Create directory for custom modules
RUN mkdir -p /usr/local/WowzaStreamingEngine/lib/custom

# We'll copy the compiled module here later
# COPY ./target/SubtitleInjector.jar /usr/local/WowzaStreamingEngine/lib/custom/

# Switch back to wowza user
USER wowza

# Rest of Wowza startup remains the same