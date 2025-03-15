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

| Port  | Purpose                     |
|-------|-----------------------------|
| 53    | DNS queries (UDP/TCP)       |
| 8081  | API for PowerDNS            |
| 8082  | API for PowerDNS Recursor   |
| 5300  | Recursor queries (UDP/TCP)  |
| 80    | HTTP (for web UI)           |
| 443   | HTTPS (for secure web UI)   |

**Notes:**
- Port **53** is used for standard DNS resolution.
- Port **5300** is required for the **PowerDNS Recursor**.
- Ports **8081** and **8082** are used by the **PowerDNS API**.
- Ensure **firewall rules** allow incoming connections on these ports.
- If running behind a **reverse proxy**, configure Nginx/Apache accordingly.
