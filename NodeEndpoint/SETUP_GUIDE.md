# Cross-Platform Setup Guide

This guide explains how to set up the NodeEndpoint server on Windows, macOS, and Linux, and how nodes automatically discover each other on the network.

## Quick Start

### 1. Build and Run (All Platforms)

```bash
cd NodeEndpoint
dotnet build
dotnet run
```

That's it! The server will:
- ✅ Auto-detect your local IP address
- ✅ Start broadcasting its presence on the network
- ✅ Listen for other nodes on the network
- ✅ Configure shared storage based on your platform
- ✅ Set up keepalive connections

### 2. Verify It's Running

You should see output like this:

```
=== GeoscientistToolkit Node Endpoint Server ===
Platform: Linux
Local IP: 192.168.1.100
HTTP API: http://192.168.1.100:5000
NodeManager: 192.168.1.100:9876
Keepalive timeout: 10 minutes

[NodeManager] Started in Hybrid mode
Node Manager status: Hybrid

Starting network discovery...
[NetworkDiscovery] Broadcasting on 192.168.1.100:9877
Network discovery enabled - other nodes can find this endpoint automatically

Ready to accept connections!
Swagger UI: http://localhost:5000/swagger
```

### 3. Connect from Another Node

On a different machine on the same network:

```bash
cd NodeEndpoint
dotnet run
```

You'll see automatic discovery:

```
[Discovery] Found NodeEndpoint at 192.168.1.100:5000 (Linux)
```

**That's it!** Nodes discover each other automatically via UDP broadcast.

## Platform-Specific Details

### Windows

**Shared Storage:**
- Default: `%TEMP%\GTK_SharedData`
- Network: `\\SharedStorage\GTK_SharedData` (if available)

**Firewall:**
The first time you run, Windows Firewall may prompt you to allow the application. Click "Allow access" for:
- HTTP API (port 5000)
- NodeManager (port 9876)
- Network Discovery (port 9877 UDP)

**Setup Network Storage (Optional):**
```powershell
# Share a folder for distributed storage
New-SmbShare -Name "GTK_SharedData" -Path "C:\GTK_SharedData" -FullAccess Everyone
```

### macOS

**Shared Storage:**
- Default: `/tmp/GTK_SharedData`
- Network: `/Volumes/Shared/GTK_SharedData` (if available)

**Firewall:**
macOS may prompt for network access. Click "Allow" when prompted.

**Setup Network Storage (Optional):**
```bash
# Mount NFS share (if you have NFS server)
sudo mkdir -p /Volumes/Shared
sudo mount -t nfs server:/export/GTK_SharedData /Volumes/Shared/GTK_SharedData
```

### Linux

**Shared Storage:**
- Default: `/tmp/GTK_SharedData`
- Network: `/mnt/shared/GTK_SharedData` (if available)

**Firewall (if using ufw):**
```bash
# Allow NodeEndpoint ports
sudo ufw allow 5000/tcp comment 'NodeEndpoint HTTP API'
sudo ufw allow 9876/tcp comment 'NodeManager'
sudo ufw allow 9877/udp comment 'Network Discovery'
```

**Setup Network Storage (NFS - Recommended):**

On storage server:
```bash
# Install NFS server
sudo apt install nfs-kernel-server

# Create shared directory
sudo mkdir -p /export/GTK_SharedData
sudo chmod 777 /export/GTK_SharedData

# Configure NFS export
echo "/export/GTK_SharedData *(rw,sync,no_subtree_check,no_root_squash)" | sudo tee -a /etc/exports

# Restart NFS
sudo exportfs -a
sudo systemctl restart nfs-kernel-server
```

On worker nodes:
```bash
# Install NFS client
sudo apt install nfs-common

# Mount shared storage
sudo mkdir -p /mnt/shared
sudo mount server:/export/GTK_SharedData /mnt/shared/GTK_SharedData

# Auto-mount on boot (add to /etc/fstab)
echo "server:/export/GTK_SharedData /mnt/shared/GTK_SharedData nfs defaults 0 0" | sudo tee -a /etc/fstab
```

**Setup Network Storage (SMB/CIFS - Alternative):**

On storage server:
```bash
# Install Samba
sudo apt install samba

# Configure share
sudo mkdir -p /srv/GTK_SharedData
sudo chmod 777 /srv/GTK_SharedData

# Add to /etc/samba/smb.conf
cat << EOF | sudo tee -a /etc/samba/smb.conf
[GTK_SharedData]
   path = /srv/GTK_SharedData
   browseable = yes
   read only = no
   guest ok = yes
EOF

# Restart Samba
sudo systemctl restart smbd
```

On worker nodes:
```bash
# Install CIFS utilities
sudo apt install cifs-utils

# Mount share
sudo mkdir -p /mnt/shared
sudo mount -t cifs //server/GTK_SharedData /mnt/shared/GTK_SharedData -o guest
```

## Configuration

### Custom Shared Storage Path

Edit `appsettings.json`:

```json
{
  "SharedStorage": {
    "Path": "/your/custom/path/GTK_SharedData",
    "UseNetworkStorage": true
  }
}
```

### Disable Network Discovery

If you're in a restricted network environment:

```json
{
  "NetworkDiscovery": {
    "Enabled": false
  }
}
```

Then manually configure worker nodes with the host IP:

```json
{
  "NodeManager": {
    "HostAddress": "192.168.1.100"
  }
}
```

### Change Ports

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:8080"
      }
    }
  },
  "NodeManager": {
    "ServerPort": 9999
  },
  "NetworkDiscovery": {
    "DiscoveryPort": 9988
  }
}
```

## Network Requirements

### Minimal Setup (No Shared Storage)
- All nodes on same local network
- UDP broadcast enabled (ports default to 9877)
- Data files must be accessible via file paths (e.g., each node has its own data)

### Optimal Setup (With Shared Storage)
- NFS or SMB/CIFS shared storage mounted on all nodes
- All nodes can read/write to shared path
- Large datasets registered once, accessible by all nodes
- Significant performance improvement for distributed jobs

## Troubleshooting

### Nodes not discovering each other

**Check network connectivity:**
```bash
# Ping other node
ping 192.168.1.100

# Check if UDP broadcast works
# On receiver:
nc -ul 9877

# On sender:
echo "test" | nc -u -b 192.168.1.255 9877
```

**Check firewall:**
```bash
# Linux
sudo ufw status

# macOS
sudo /usr/libexec/ApplicationFirewall/socketfilterfw --getglobalstate

# Windows
Get-NetFirewallProfile
```

**Manually connect:**
Edit `appsettings.json` on worker nodes:
```json
{
  "NodeManager": {
    "HostAddress": "192.168.1.100"
  },
  "NetworkDiscovery": {
    "Enabled": false
  }
}
```

### Shared storage not accessible

**Test network storage:**
```bash
# NFS
showmount -e server

# SMB/CIFS
smbclient -L //server

# Test write
touch /mnt/shared/GTK_SharedData/test.txt
```

**Check permissions:**
```bash
ls -ld /mnt/shared/GTK_SharedData
# Should show: drwxrwxrwx or similar
```

**Fallback to local storage:**
Each node will use its local temp directory if network storage is unavailable. This works but:
- Large datasets must be copied to each node
- Less efficient for distributed jobs
- No shared result caching

### Port already in use

**Find what's using the port:**
```bash
# Linux/macOS
lsof -i :5000
sudo netstat -tulpn | grep 5000

# Windows
netstat -ano | findstr :5000
```

**Change the port:**
Edit `appsettings.json` and change the port numbers.

## Performance Tips

### 1. Use Network Storage
- 10-100x faster than transferring large files over network
- Essential for multi-GB CT volumes
- Recommended: NFS on Linux/macOS, SMB on Windows

### 2. Gigabit Network
- Minimum: 1 Gbps ethernet
- Recommended: 10 Gbps for large CT volumes
- Avoid Wi-Fi for worker nodes (inconsistent performance)

### 3. Node Placement
- Place nodes on same subnet for best discovery
- Minimize network hops to storage server
- Consider dedicated storage network (VLAN)

### 4. Resource Allocation
- Server typically runs 2x oversubscription (16 cores = 32 partition slots)
- Each worker should have sufficient RAM for partition size
- Monitor with `/api/node` endpoint

## Testing Your Setup

### 1. Check node discovery
```bash
curl http://localhost:5000/api/node
# Should show all discovered nodes
```

### 2. Submit a test job
```bash
# Register test data
curl -X POST http://localhost:5000/api/partitionedjob/register-data \
  -H "Content-Type: application/json" \
  -d '{
    "filePath": "/path/to/test.raw",
    "dataType": "CTVolume",
    "width": 512, "height": 512, "depth": 512
  }'

# Submit partitioned job
curl -X POST http://localhost:5000/api/partitionedjob/submit \
  -H "Content-Type: application/json" \
  -d '{
    "jobType": "CTFiltering",
    "dataReferenceId": "REFERENCE_ID_FROM_ABOVE",
    "partitionCount": 4,
    "parameters": {"filterType": "Gaussian"}
  }'
```

### 3. Monitor progress
```bash
# Check status
curl http://localhost:5000/api/partitionedjob/JOB_ID/status

# View sub-jobs
curl http://localhost:5000/api/partitionedjob/JOB_ID/sub-jobs
```

## Example: 3-Node Cluster Setup

### Node 1 (Storage + Endpoint)
```bash
# Setup NFS share
sudo apt install nfs-kernel-server
sudo mkdir -p /export/GTK_SharedData
sudo chmod 777 /export/GTK_SharedData
echo "/export/GTK_SharedData *(rw,sync,no_subtree_check)" | sudo tee -a /etc/exports
sudo exportfs -a

# Run endpoint
cd NodeEndpoint
dotnet run
```

### Node 2 & 3 (Workers)
```bash
# Mount NFS share
sudo apt install nfs-common
sudo mkdir -p /mnt/shared
sudo mount node1:/export/GTK_SharedData /mnt/shared/GTK_SharedData

# Run endpoint
cd NodeEndpoint
dotnet run
```

All nodes will discover each other automatically!

## Security Considerations

**For production deployments:**

1. **Use firewall rules** to restrict NodeEndpoint access
2. **Consider VPN** for cross-site deployments
3. **Secure NFS with Kerberos** or restrict by IP
4. **Add authentication** to HTTP API (JWT, API keys)
5. **Use HTTPS/TLS** for encrypted communication
6. **Network isolation** - dedicated VLAN for compute cluster

**For development/internal use:**
- Current setup is sufficient
- Trusted network assumption
- No authentication required

## Summary

✅ **Cross-Platform**: Works on Windows, macOS, and Linux
✅ **Auto-Discovery**: Nodes find each other automatically via UDP broadcast
✅ **Auto-Configuration**: Detects local IP, sets up paths automatically
✅ **Shared Storage**: Platform-specific defaults with network storage support
✅ **Firewall Friendly**: Clear port requirements, easy to configure
✅ **Zero Config**: Just run `dotnet run` and it works!

For most users: **Just run it!** The defaults are sensible and will work out of the box on the same network.
