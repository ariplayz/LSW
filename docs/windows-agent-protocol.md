# LSW Agent Protocol

## Transport: virtio-serial

The agent communicates over a QEMU virtio-serial port named **`org.lsw.agent`**.

### Host (lswd) side

The QEMU command line includes:

```
-device virtio-serial-pci
-chardev socket,id=lswagent,path=/run/user/$UID/lsw/<name>.agent.sock,server=on,wait=off
-device virtserialport,chardev=lswagent,name=org.lsw.agent
```

The host daemon connects to `.agent.sock` and writes/reads framed JSON-RPC messages.

### Guest (Windows) side

On Windows with the virtio-win driver, the port appears as:

```
\\.\Global\org.lsw.agent
```

The agent opens this path as a `FileStream`. If your driver maps it to a COM port instead (e.g. `COM3`), set:

```
LSW_SERIAL_PORT=COM3
```

as a Machine-scope environment variable and restart the service.

---

## Framing: length-prefix

Every message (request or response) is framed as:

```
+----------+-------- ... --------+
|  4 bytes | N bytes             |
| LE uint32| UTF-8 JSON payload  |
+----------+-------- ... --------+
```

- **Length field**: 4-byte little-endian unsigned 32-bit integer = byte length of JSON payload.
- **Payload**: UTF-8 encoded JSON (no BOM).
- Maximum frame size: 64 MiB (enforced by the agent; larger frames are rejected).
- No newline required after payload, but including one is harmless.

### Example (hex dump)

Request `{"jsonrpc":"2.0","id":1,"method":"handshake","params":{"token":"abc"}}`:

```
payload = 70 bytes
frame   = 46 00 00 00  <70 bytes UTF-8 JSON>
```

---

## JSON-RPC 2.0

All messages conform to [JSON-RPC 2.0](https://www.jsonrpc.org/specification).

### Request

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "handshake",
  "params": { "token": "<per-vm-token>" }
}
```

### Success response

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "ok": true,
    "agent_version": "0.1.0",
    "capabilities": ["handshake", "ensure_ssh", "mount_share", "run_cmd_shell", ...]
  }
}
```

### Error response

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "error": { "code": -32003, "message": "authentication failed" }
}
```

---

## Auth model

1. Any method called before a successful `handshake` returns error **-32001 not authenticated**.
2. `handshake` accepts any non-empty token on the **first call** and stores it.
3. Subsequent `handshake` calls require the same token; a wrong token returns **-32003**.
4. Token rotation: send `{ "token": "<current>", "new_token": "<new>" }` to `handshake`.
5. The token is **never** logged or persisted by the agent.

---

## Complete example flow

```
Host                                   Guest agent
 |                                         |
 |--- handshake({token}) ---------------->|
 |<-- {ok:true, capabilities:[...]} ------|
 |                                         |
 |--- ensure_ssh({public_key, username}) ->|
 |<-- {ok:true, details:"..."} ------------|
 |                                         |
 |--- mount_share({backend:"smb",        ->|
 |       tag_or_unc:"\\10.0.2.2\lsw_home",|
 |       guest_path:"D:\\home\\alice"}) ---|
 |<-- {ok:true, details:"..."} ------------|
 |                                         |
 |=== host SSH-connects on port 2222 ======|
 |=== PowerShell prompt at D:\home\alice ==|
```
