# Environment Constraints

## File System
- Work directory: /tmp/cc-workspaces/{userId}/ (your userId is in context)
- You may create subdirectories within this prefix
- You may NOT access /home/, /etc/, /var/, /root/, or any system paths

## Python Environment
- Python 3.x available
- Standard library available
- Key packages: python-docx, openpyxl, python-pptx, requests (for approved APIs only)

## No External Network
- No direct HTTP/HTTPS calls to external services
- All external data access must go through the MCP servers in your context
