"""JARVIS FIRE desktop training login. Python 3, standard library only."""
import hashlib
import hmac
import tkinter as tk
from tkinter import ttk, messagebox

PASSWORD_SHA256 = "5e730980a8eef004aa65f3b8309f9ccd623abbcbd429b53b87b24bd6bb491c58"
SOURCES = [
    ("Учебный гидрант №1", "45.0433, 41.9690"),
    ("Учебный гидрант №2", "45.0461, 41.9728"),
    ("Учебный гидрант №3", "45.0407, 41.9652"),
]

root = tk.Tk()
root.title("ДЖАРВИС FIRE — учебная диспетчерская")
root.geometry("630x410")
frame = ttk.Frame(root, padding=25)
frame.pack(fill="both", expand=True)
ttk.Label(frame, text="ДЖАРВИС FIRE", font=("Arial", 22, "bold")).pack(pady=12)
ttk.Label(frame, text="УЧЕБНЫЙ ПРОТОТИП. Не использовать при реальных вызовах.").pack(pady=8)
ttk.Label(frame, text="Логин").pack()
login = ttk.Entry(frame)
login.pack(pady=4)
ttk.Label(frame, text="Пароль").pack()
password = ttk.Entry(frame, show="*")
password.pack(pady=4)

def sign_in():
    digest = hashlib.sha256(password.get().encode("utf-8")).hexdigest()
    if login.get().strip() != "admin" or not hmac.compare_digest(digest, PASSWORD_SHA256):
        messagebox.showerror("Вход", "Неверный логин или пароль")
        return
    for child in frame.winfo_children():
        child.destroy()
    ttk.Label(frame, text="Демонстрационная диспетчерская — администратор",
              font=("Arial", 14, "bold")).pack(pady=10)
    ttk.Label(frame, text="Учебные водоисточники (координаты вымышлены):").pack(pady=8)
    for name, coords in SOURCES:
        ttk.Label(frame, text=f"{name} — {coords}").pack(anchor="w", pady=5)
    ttk.Button(frame, text="Выйти", command=sign_out).pack(pady=20)

def sign_out():
    root.destroy()

ttk.Button(frame, text="Войти", command=sign_in).pack(pady=16)
root.bind("<Return>", lambda _: sign_in() if login.winfo_exists() else None)
root.mainloop()
