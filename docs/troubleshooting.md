# Troubleshooting

This section provides solutions to common issues encountered while using **PowerNS Web**. If you experience any problems, follow the steps below before seeking further assistance.

---

## 📄 **Where to Find Logs?**
PowerNS Web maintains logs in the **`logs/`** directory located in the root of the application (default: `/var/www/powerdns-web`).  

If you encounter an issue, provide the relevant log details when reporting a problem.

---

## 🔧 **Common Issues and Solutions**

### ❌ PowerNS Web Fails to Start  
**Possible Causes & Fixes:**
1. **Configuration file missing or invalid**  
   - Check if the configuration file exists:  
     ```bash
     ls /var/www/powerdns-web/appsettings.json
     ```
   - If missing, run:
     ```bash
     powerdnsweb configure
     ```
   - If the file is incorrect, check logs for syntax errors.

2. **Database Connection Issues**  
   - Ensure that your MySQL/MariaDB server is running:
     ```bash
     systemctl status mysql
     ```
   - Verify that the database credentials in **`config.json`** are correct.

3. **Port Conflict**  
   - Make sure no other service is using the same port:
     ```bash
     netstat -tulnp | grep :53
     ```
   - Change the listening port in the configuration if needed.

---

### 🛑 **Cannot Log In**
**Possible Causes & Fixes:**
1. **Wrong Credentials**  
   - Ensure you are using the correct username and password.

2. **Forgot password?**  
   - Run:
     ```bash
     mysql -u root -p -e "DELETE FROM users;"
     ```
   - This command will delete all users, allowing you to create a new one (via login form).

---

### ⚠️ **DNS Records Not Updating**
**Possible Causes & Fixes:**
1. **PowerDNS API Not Responding**  
   - Check if PowerDNS is running:
     ```bash
     systemctl status pdns
     ```
   - Verify API connectivity:
     ```bash
     curl -H "X-API-Key: your_api_key" http://localhost:8081/api/v1/servers/localhost
     ```

2. **Recursor cache Issues**  
   - Clear DNS cache:
     ```bash
     rec_control wipe-cach
     ```

---

### 🖥 **Recursor Stats Not Displaying**
**Possible Causes & Fixes:**
1. **Recursor API Not Enabled**  
   - Ensure recursor API is enabled in **`recursor.conf`**:
     ```
     api=yes
     api-key=your_api_key
     ```
   - Restart PowerDNS Recursor:
     ```bash
     systemctl restart pdns-recursor
     ```

---

## 🛠 **Debugging Tips**
1. **Enable Debug Mode**  
   Edit **`appsettings.json`** and set:
   ```json
   "debug": true
   ```
   Restart the service for changes to take effect.

2. **Manually Test API Requests**  
   ```bash
   curl -H "X-API-Key: your_api_key" http://localhost:8081/api/v1/servers/localhost
   ```

If the issue persists, check the logs in the **`logs/`** folder and provide details when reporting the problem.
