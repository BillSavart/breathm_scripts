# self_check.py
import time
import sys
import RPi.GPIO as GPIO

try:
    from smbus2 import SMBus
except ImportError:
    from smbus import SMBus
from bmp280 import BMP280

I2C_BUS_ID = 1             
BMP280_I2C_ADDR = 0x76     

IN1_PIN = 23   
IN2_PIN = 24   
ENA_PIN = 25   

EXTEND_TIME = 3  
RETRACT_TIME = 3 
TEST_SPEED = 80    

def self_check_bmp280():
    """
    檢查 BMP280 壓力感測器是否正常工作。
    
    行為:
    - 印出檢查訊息。
    - 嘗試創建 SMBus 和 BMP280 實例，讀取壓力值。
    - 如果成功，印出壓力值並返回 True。
    - 如果失敗，印出錯誤訊息並返回 False。
    """
    print("[SELF-CHECK] Checking BMP280...")

    try:
        bus = SMBus(I2C_BUS_ID)
        bmp280 = BMP280(i2c_dev=bus, i2c_addr=BMP280_I2C_ADDR)
        pressure = bmp280.get_pressure()
        print(f"[SELF-CHECK] BMP280 reads data successfully: {pressure:.2f} hPa")
        return True

    except Exception as e:
        print(f"[SELF-CHECK] BMP280 自檢失敗：{e}")
        return False


def setup_motor_gpio():
    """
    設置馬達控制的 GPIO 引腳和 PWM。
    
    返回: PWM 實例，用於控制馬達速度。
    
    行為:
    - 設置 GPIO 模式為 BCM。
    - 配置 IN1_PIN、IN2_PIN 和 ENA_PIN 為輸出。
    - 創建 PWM 實例在 ENA_PIN 上，頻率 800Hz，啟動占空比 100%。
    - 返回 PWM 實例供後續使用。
    """
    GPIO.setmode(GPIO.BCM)
    GPIO.setup(IN1_PIN, GPIO.OUT)
    GPIO.setup(IN2_PIN, GPIO.OUT)
    GPIO.setup(ENA_PIN, GPIO.OUT)

    pwm = GPIO.PWM(ENA_PIN, 800) 
    pwm.start(100)
    return pwm


def motor_stop(pwm):
    """
    停止馬達運動。
    
    參數:
    - pwm: PWM 實例。
    
    行為:
    - 設置 IN1_PIN 和 IN2_PIN 為低電平，停止馬達。
    - 設置 PWM 占空比為 0，關閉電源。
    """
    GPIO.output(IN1_PIN, GPIO.LOW)
    GPIO.output(IN2_PIN, GPIO.LOW)
    pwm.ChangeDutyCycle(0)


def motor_extend(pwm, seconds):
    """
    讓馬達伸出指定時間。
    
    參數:
    - pwm: PWM 實例。
    - seconds: 伸出持續時間（秒）。
    
    行為:
    - 印出伸出訊息。
    - 設置 IN1_PIN 高、IN2_PIN 低，啟動伸出。
    - 設置 PWM 占空比為 TEST_SPEED。
    - 等待指定時間。
    - 調用 motor_stop 停止。
    """
    print("[SELF-CHECK] Motor: extending...")
    GPIO.output(IN1_PIN, GPIO.HIGH)
    GPIO.output(IN2_PIN, GPIO.LOW)
    pwm.ChangeDutyCycle(TEST_SPEED)
    time.sleep(seconds)
    motor_stop(pwm)


def motor_retract(pwm, seconds):
    """
    讓馬達縮回指定時間。
    
    參數:
    - pwm: PWM 實例。
    - seconds: 縮回持續時間（秒）。
    
    行為:
    - 印出縮回訊息。
    - 設置 IN1_PIN 低、IN2_PIN 高，啟動縮回。
    - 設置 PWM 占空比為 TEST_SPEED。
    - 等待指定時間。
    - 調用 motor_stop 停止。
    """
    print("[SELF-CHECK] Motor: Retracting...")
    GPIO.output(IN1_PIN, GPIO.LOW)
    GPIO.output(IN2_PIN, GPIO.HIGH)
    pwm.ChangeDutyCycle(TEST_SPEED)
    time.sleep(seconds)
    motor_stop(pwm)


def self_check_actuator():
    """
    檢查線性致動器（馬達）是否正常工作。
    
    返回: 如果檢查通過，返回 True；否則 False。
    
    行為:
    - 印出檢查訊息。
    - 調用 setup_motor_gpio 初始化 GPIO 和 PWM。
    - 停止馬達並等待 0.5 秒。
    - 伸出馬達 EXTEND_TIME 秒。
    - 等待 0.5 秒。
    - 縮回馬達 RETRACT_TIME 秒。
    - 等待 0.5 秒。
    - 印出成功訊息並返回 True。
    - 如果異常，印出錯誤並返回 False。
    - 最終停止 PWM 並清理 GPIO。
    """
    print("[SELF-CHECK] Check L298N Actuator...")

    pwm = None
    try:
        pwm = setup_motor_gpio()

        motor_stop(pwm)
        time.sleep(0.5)

        motor_extend(pwm, EXTEND_TIME)
        time.sleep(0.5)

        motor_retract(pwm, RETRACT_TIME)
        time.sleep(0.5)

        print("[SELF-CHECK] Linear actuator operates successfully.")
        return True

    except Exception as e:
        print(f"[SELF-CHECK] Linear actuator fails: {e}")
        return False

    finally:
        if pwm is not None:
            pwm.stop()
        GPIO.cleanup()


def run_self_check():
    """
    運行完整的自檢過程，檢查 BMP280 和線性致動器。
    
    返回: 如果所有檢查通過，返回 True；否則 False。
    
    行為:
    - 印出開始訊息。
    - 調用 self_check_bmp280 檢查感測器。
    - 調用 self_check_actuator 檢查馬達。
    - 根據結果印出通過或失敗訊息。
    - 如果全部通過，印出 "Everything looks good!"；否則 "Something is wrong!"。
    - 印出結束訊息。
    - 返回整體結果。
    """
    print("========== Start self-checking ==========")

    bmp_ok = self_check_bmp280()
    if not bmp_ok:
        print("[SELF-CHECK] BMP280 self-checking fails.")
    else:
        print("[SELF-CHECK] BMP280 self-checking passes.")

    motor_ok = self_check_actuator()
    if not motor_ok:
        print("[SELF-CHECK] Linear actuator self-checking fails.")
    else:
        print("[SELF-CHECK] Linear actuator self-checking passes.")

    all_ok = bmp_ok and motor_ok

    if all_ok:
        print("Everything looks good!")
    else:
        print("Something is wrong!")

    print("========== End of self-checking ==========")
    return all_ok


if __name__ == "__main__":
    ok = run_self_check()
    if not ok:
        sys.exit(1)