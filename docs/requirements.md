## System Requirements

### Hardware Requirements

- **CPU**: 1 core
- **RAM**: 2 GB

---

### Software Requirements

- **Operating System**:
  - Debian (latest stable release)
  - Ubuntu (latest LTS release)
- **Required Packages (for manual install)**:
  - `PowerDNS`
  - `PowerDNS Recursor`
  - `Nginx` or `Apache` (for web UI)
  - `MariaDB` or `MySQL`
  - `systemd` (default in modern Debian/Ubuntu)

---

### Network Requirements

Ensure the following ports are open and available:

| Port  | Purpose                     | Notes                                                                               |
|-------|-----------------------------|-------------------------------------------------------------------------------------|
| 53    | DNS queries (UDP/TCP)       | Used for **PowerDNS Recursor** (if enabled) otherwise **PowerDNS Authoritative**    |
| 5300  | DNS queries (UDP/TCP)       | Used for **PowerDNS Authoritative** if **PowerDNS Recursor** enabled                |
| 8081  | API for PowerDNS            | 
| 8082  | API for PowerDNS Recursor   |
| 80    | HTTP (for web UI)           |
| 443   | HTTPS (for secure web UI)   |

**Notes:**
- Ensure **firewall rules** allow incoming connections on these ports.  
- If running behind a **reverse proxy**, configure Nginx/Apache accordingly.  
