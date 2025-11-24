#!/bin/bash

# Start Wowza initialization in background
/sbin/init.d/WowzaStreamingEngine start-foreground &
WOWZA_PID=$!

# Wait for lib directory to be initialized
echo "Waiting for Wowza to initialize lib directory..."
for i in {1..30}; do
    if [ -d "/usr/local/WowzaStreamingEngine/lib" ] && [ "$(ls -A /usr/local/WowzaStreamingEngine/lib 2>/dev/null)" ]; then
        echo "Lib directory initialized!"
        break
    fi
    echo "Still waiting... ($i/30)"
    sleep 2
done

# Copy custom module after initialization
if [ -f /tmp/subtitle-injector-1.0-SNAPSHOT.jar ]; then
    echo "Copying custom module to lib directory..."
    cp /tmp/subtitle-injector-1.0-SNAPSHOT.jar /usr/local/WowzaStreamingEngine/lib/subtitle-injector-1.0-SNAPSHOT.jar
    chmod 644 /usr/local/WowzaStreamingEngine/lib/subtitle-injector-1.0-SNAPSHOT.jar
    echo "Custom module installed successfully!"
else
    echo "WARNING: JAR file not found at /tmp/subtitle-injector-1.0-SNAPSHOT.jar"
fi

# Wait for Wowza process
wait $WOWZA_PID