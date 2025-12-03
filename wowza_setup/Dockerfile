FROM ubuntu:latest

# Install wget, curl, AND dos2unix (to fix Windows line endings)
RUN apt-get update && \
    apt-get install -y wget curl dos2unix && \
    rm -rf /var/lib/apt/lists/*

COPY setup_script.sh /wowza_setup/

# Run dos2unix to strip Windows \r characters, then make executable
RUN dos2unix /wowza_setup/setup_script.sh && \
    chmod +x /wowza_setup/setup_script.sh

ENTRYPOINT ["/wowza_setup/setup_script.sh"]