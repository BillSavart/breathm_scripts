import socket
import threading
import subprocess
import os
import sys
import time
from self_check import run_self_check

HOST = "0.0.0.0"
PORT = 5005
PROCESS_STOP_TIMEOUT = 5.0

breathm_process = None
active_conn = None
active_addr = None
process_lock = threading.Lock()
active_conn_lock = threading.Lock()

# 1. 取得絕對路徑，確保不管在哪執行都能找到 fix_version.py
CURRENT_DIR = os.path.dirname(os.path.abspath(__file__))
SCRIPT_PATH = os.path.join(CURRENT_DIR, "fix_version.py")

def set_active_client(conn, addr):
    global active_conn, active_addr
    with active_conn_lock:
        active_conn = conn
        active_addr = addr
        print(f"[SERVER] Active Unity client set to {addr}")


def clear_active_client(conn=None):
    global active_conn, active_addr
    with active_conn_lock:
        if conn is not None and active_conn is not conn:
            return False
        old_addr = active_addr
        active_conn = None
        active_addr = None
        if old_addr is not None:
            print(f"[SERVER] Active Unity client cleared: {old_addr}")
        return True


def send_sync_to_active_client(msg):
    with active_conn_lock:
        conn = active_conn
        addr = active_addr

    if conn is None:
        print("[SERVER] No active Unity client for sync data; stopping script for safety")
        stop_breathing_process("No active Unity client")
        return False

    try:
        conn.sendall(msg.encode("utf-8"))
        return True
    except Exception as e:
        print(f"[SERVER] Failed to send sync data to {addr}: {e}")
        clear_active_client(conn)
        stop_breathing_process("Lost Unity client while sending sync data")
        return False


def stop_breathing_process(reason="Stop requested"):
    global breathm_process

    with process_lock:
        proc = breathm_process
        if proc is None or proc.poll() is not None:
            breathm_process = None
            return False

        print(f"[SERVER] Stopping breathing script: {reason}")
        proc.terminate()
        deadline = time.time() + PROCESS_STOP_TIMEOUT

        while proc.poll() is None and time.time() < deadline:
            time.sleep(0.1)

        if proc.poll() is None:
            print("[SERVER] Script did not exit after SIGTERM; killing it")
            proc.kill()
            proc.wait()

        breathm_process = None
        print("[SERVER] Breathing script stopped")
        return True


def monitor_process_output(proc):
    """
    持續監視子程序的輸出（包括 stdout 和 stderr），並將包含 'SYNC_' 關鍵字的行通過 socket 發送給 Unity 客戶端。
    
    參數:
    - proc: 子程序對象（subprocess.Popen 實例），用於讀取其輸出。
    行為:
    - 使用迭代器逐行讀取 proc.stdout。
    - 每行輸出都會被印出到伺服器控制台（用於調試）。
    - 如果行包含 'SYNC_'，則將該行（加上換行符）發送給目前 active 的 Unity client。
    - 如果發送失敗，記錄錯誤並中斷監視。
    - 當子程序結束時，退出循環並記錄結束訊息。
    """
    try:
        # 逐行讀取輸出
        for line in iter(proc.stdout.readline, ''):
            if not line:
                break
            line = line.strip()
            
            # [關鍵] 印出所有 Log，這樣你才看得到它有沒有在跑，或有沒有報錯
            print(f"[SCRIPT Log] {line}") 

            if "SYNC_" in line:
                msg = line + "\n"
                if not send_sync_to_active_client(msg):
                    break
    except Exception as e:
        print(f"[SERVER] Monitor thread error: {e}")
    finally:
        global breathm_process
        with process_lock:
            if breathm_process is proc and proc.poll() is not None:
                breathm_process = None
        print("[SERVER] Process monitor ended")

def start_breathing_process(conn, addr):
    global breathm_process

    with process_lock:
        if breathm_process is not None and breathm_process.poll() is None:
            set_active_client(conn, addr)
            return "INFO: Script already running; attached to this client\n"

        print(f"[SERVER] Attempting to start script: {SCRIPT_PATH}")

        try:
            # sys.executable: 確保使用目前的 Python 環境 (venv)
            # "-u": 強制不緩衝，讓 print 馬上顯示
            # stderr=subprocess.STDOUT: 讓錯誤訊息也顯示在 Log 裡
            breathm_process = subprocess.Popen(
                [sys.executable, "-u", SCRIPT_PATH],
                stdout=subprocess.PIPE,
                stderr=subprocess.STDOUT,
                text=True,
                bufsize=1
            )
            set_active_client(conn, addr)

            t = threading.Thread(target=monitor_process_output, args=(breathm_process,), daemon=True)
            t.start()

            return "OK: ACTIVATE\n"
        except Exception as e:
            breathm_process = None
            clear_active_client(conn)
            print(f"[SERVER] Failed to start process: {e}")
            return f"ERROR: Launch failed {e}\n"


def handle_command(cmd: str, conn, addr):
    cmd = cmd.strip()
    print(f"[SERVER] Received command: {cmd}")

    if cmd == "ACTIVATE":
        return start_breathing_process(conn, addr)

    elif cmd == "DEACTIVATE":
        stopped = stop_breathing_process("DEACTIVATE command")
        clear_active_client(conn)
        if stopped:
            return "OK: DEACTIVATE\n"
        else:
            return "INFO: Script is NOT running\n"
    else:
        return "ERROR: UNKNOWN_COMMAND\n"

def client_thread(conn, addr):
    print(f"[SERVER] New connection from {addr}")
    with conn:
        buffer = b""
        while True:
            try:
                data = conn.recv(1024)
                if not data:
                    print(f"[SERVER] Client {addr} disconnected")
                    break
                buffer += data
                while b"\n" in buffer:
                    line, buffer = buffer.split(b"\n", 1)
                    response = handle_command(line.decode("utf-8"), conn, addr)
                    conn.sendall(response.encode("utf-8"))
            except ConnectionResetError:
                print(f"[SERVER] Connection reset by {addr}")
                break
            except Exception as e:
                print(f"[SERVER] Error: {e}")
                break
        if clear_active_client(conn):
            stop_breathing_process(f"Unity client {addr} disconnected")

def main():
    if not run_self_check():
        print("[SERVER] Self-check fails. System terminates")
        return

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
