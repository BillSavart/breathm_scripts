# 修改版
import socket
import threading
import time
from self_check import run_self_check

# [關鍵] 匯入修改後的 fix_version
import fix_version 

HOST = "0.0.0.0" # 建議改為 0.0.0.0 讓所有 IP 都能連
PORT = 5005

# 用來控制執行緒
breath_thread = None
stop_event = threading.Event()
active_connection = None # 儲存當前的連線以便回傳資料

def send_to_unity(message):
    """這就是我們會傳進 fix_version 的 callback"""
    global active_connection
    if active_connection:
        try:
            active_connection.sendall(message.encode("utf-8"))
        except Exception as e:
            print(f"Send Error: {e}")

def handle_command(cmd: str):
    global breath_thread, stop_event

    cmd = cmd.strip()
    print(f"[SERVER] Received command: {cmd}")

    if cmd == "ACTIVATE":
        if breath_thread is None or not breath_thread.is_alive():
            stop_event.clear()
            # [關鍵] 啟動執行緒，並傳入 callback
            breath_thread = threading.Thread(
                target=fix_version.main, 
                kwargs={'stop_event': stop_event, 'msg_callback': send_to_unity},
                daemon=True
            )
            breath_thread.start()
            return "OK: ACTIVATE\n"
        else:
            return "INFO: Already running\n"

    elif cmd == "DEACTIVATE":
        if breath_thread and breath_thread.is_alive():
            stop_event.set() # 通知執行緒停止
            breath_thread.join(timeout=2.0)
            return "OK: DEACTIVATE\n"
        else:
            return "INFO: Not running\n"
    else:
        return "ERROR: UNKNOWN\n"

def client_thread(conn, addr):
    global active_connection
    print(f"[SERVER] New connection from {addr}")
    active_connection = conn # 綁定連線

    with conn:
        buffer = b""
        while True:
            try:
                data = conn.recv(1024)
                if not data: break
                buffer += data
                while b"\n" in buffer:
                    line, buffer = buffer.split(b"\n", 1)
                    response = handle_command(line.decode("utf-8"))
                    conn.sendall(response.encode("utf-8"))
            except ConnectionResetError:
                break
    
    print(f"[SERVER] Client {addr} disconnected")
    active_connection = None

def main():
    if not run_self_check(): return
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        s.bind((HOST, PORT))
        s.listen(1)
        print(f"[SERVER] Listening on {HOST}:{PORT}")
        while True:
            conn, addr = s.accept()
            t = threading.Thread(target=client_thread, args=(conn, addr), daemon=True)
            t.start()

if __name__ == "__main__":
    main()