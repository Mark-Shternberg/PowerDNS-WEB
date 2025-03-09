function validateZoneName(zoneName) {
    const zoneRegex = /^(?!-)([a-zA-Z0-9-]{1,63}\.)+[a-zA-Z]{2,63}$/;

    if (zoneName === "") {
        return "Please enter a zone name.";
    }

    if (zoneName.length > 255) {
        return "Domain name is too long (max 255 characters).";
    }

    if (zoneName.includes("..")) {
        return "Domain name cannot contain consecutive dots.";
    }

    if (!zoneRegex.test(zoneName)) {
        return "Invalid domain format. Use letters, numbers, and hyphens.";
    }

    return "";
}

function validateIPv4(ip) {
    const ipv4Pattern = /^(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])(\.(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])){3}$/;

    if (ip === "") {
        return "Please enter IP address.";
    }

    if (ipv4Pattern.test(ip)) {
        return "";
    }
    else return "Invalid IP address format.";
}

function validateIPv6(ip) {
    const ipv6Pattern = /^(([0-9A-Fa-f]{1,4}:){7}([0-9A-Fa-f]{1,4}|:)|(([0-9A-Fa-f]{1,4}:){1,7}:)|(([0-9A-Fa-f]{1,4}:){1,6}:[0-9A-Fa-f]{1,4})|(([0-9A-Fa-f]{1,4}:){1,5}(:[0-9A-Fa-f]{1,4}){1,2})|(([0-9A-Fa-f]{1,4}:){1,4}(:[0-9A-Fa-f]{1,4}){1,3})|(([0-9A-Fa-f]{1,4}:){1,3}(:[0-9A-Fa-f]{1,4}){1,4})|(([0-9A-Fa-f]{1,4}:){1,2}(:[0-9A-Fa-f]{1,4}){1,5})|([0-9A-Fa-f]{1,4}:((:[0-9A-Fa-f]{1,4}){1,6}))|(:((:[0-9A-Fa-f]{1,4}){1,7}|:))|(::([Ff]{4}(:0{1,4}){0,1}:){0,1}((25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9])\.){3,3}(25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9]))|([0-9A-Fa-f]{1,4}:([0-9A-Fa-f]{1,4}:){0,5}((25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9])\.){3,3}(25[0-5]|(2[0-4]|1{0,1}[0-9]){0,1}[0-9])))$/;

    if (ip === "") {
        return "Please enter IP address.";
    }

    if (ipv6Pattern.test(ip)) {
        return "";
    }
    else return "Invalid IP address format.";
}

function validateSubdomain(subdomain) {
    // (a-z, 0-9, -, but "-" not at the start/end)
    const subdomainPattern = /^(?!-)[a-z0-9-]{1,63}(?<!-)$/;

    if (subdomain === "") {
        return "Please enter subdomain.";
    }

    if (subdomainPattern.test(subdomain)) {
        return "";
    }
    return "Invalid subdomain name.";
}