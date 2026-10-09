#!/bin/bash
for i in {0..9}; do
    test -S "$XDG_RUNTIME_DIR/discord-ipc-$i" || ln -sf "$XDG_RUNTIME_DIR/app/com.discordapp.Discord/discord-ipc-$i" "$XDG_RUNTIME_DIR/discord-ipc-$i"
done

exec /app/lib/starlight-launcher/Starlight.Launcher "$@"
