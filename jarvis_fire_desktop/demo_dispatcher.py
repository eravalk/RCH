"""JARVIS FIRE — Windows training dispatcher. Local demo, not for emergency use."""
import csv
import hashlib
import hmac
import os
import sqlite3
import tkinter as tk
from datetime import datetime
from pathlib import Path
from tkinter import filedialog, messagebox, ttk

PASSWORD_SHA256 = "5e730980a8eef004aa65f3b8309f9ccd623abbcbd429b53b87b24bd6bb491c58"
DATA_DIR = Path(os.environ.get("LOCALAPPDATA", str(Path.home()))) / "JarvisFireTraining"
DATA_DIR.mkdir(parents=True, exist_ok=True)
DB_PATH = DATA_DIR / "training.db"
WARNING = "УЧЕБНАЯ ВЕРСИЯ • НЕ ИСПОЛЬЗОВАТЬ ДЛЯ РЕАЛЬНЫХ ВЫЗОВОВ"

def db():
    connection = sqlite3.connect(DB_PATH)
    connection.execute("""CREATE TABLE IF NOT EXISTS incidents (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        created_at TEXT NOT NULL,
        address TEXT NOT NULL,
        description TEXT NOT NULL,
        status TEXT NOT NULL
    )""")
    connection.execute("""CREATE TABLE IF NOT EXISTS water_sources (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        name TEXT NOT NULL,
        latitude REAL NOT NULL,
        longitude REAL NOT NULL,
        notes TEXT NOT NULL DEFAULT ''
    )""")
    if not connection.execute("SELECT 1 FROM water_sources LIMIT 1").fetchone():
        connection.executemany(
            "INSERT INTO water_sources(name, latitude, longitude, notes) VALUES (?,?,?,?)",
            [
                ("Учебный гидрант №1", 45.0433, 41.9690, "Вымышленный"),
                ("Учебный гидрант №2", 45.0461, 41.9728, "Вымышленный"),
                ("Учебный гидрант №3", 45.0407, 41.9652, "Вымышленный"),
            ],
        )
    connection.commit()
    return connection

class App(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("ДЖАРВИС FIRE — учебная диспетчерская")
        self.geometry("1050x650")
        self.minsize(820, 540)
        self.conn = db()
        self.protocol("WM_DELETE_WINDOW", self.close)
        self.login_screen()

    def clear(self):
        for child in self.winfo_children():
            child.destroy()

    def close(self):
        self.conn.close()
        self.destroy()

    def login_screen(self):
        self.clear()
        container = ttk.Frame(self, padding=35)
        container.pack(expand=True)
        ttk.Label(container, text="ДЖАРВИС FIRE", font=("Arial", 26, "bold")).pack(pady=16)
        ttk.Label(container, text=WARNING, foreground="#b00020").pack(pady=10)
        ttk.Label(container, text="Логин").pack()
        user = ttk.Entry(container, width=36)
        user.pack(pady=5)
        ttk.Label(container, text="Пароль").pack()
        password = ttk.Entry(container, show="*", width=36)
        password.pack(pady=5)
        def sign_in():
            digest = hashlib.sha256(password.get().encode("utf-8")).hexdigest()
            if user.get().strip() == "admin" and hmac.compare_digest(digest, PASSWORD_SHA256):
                self.dashboard()
            else:
                messagebox.showerror("Ошибка входа", "Неверный логин или пароль")
        ttk.Button(container, text="Войти", command=sign_in).pack(pady=18)
        password.bind("<Return>", lambda _: sign_in())
        user.focus_set()

    def dashboard(self):
        self.clear()
        top = ttk.Frame(self, padding=12)
        top.pack(fill="x")
        ttk.Label(top, text="ДЖАРВИС FIRE — диспетчер", font=("Arial", 18, "bold")).pack(side="left")
        ttk.Button(top, text="Выход", command=self.login_screen).pack(side="right")
        ttk.Label(self, text=WARNING, foreground="#b00020").pack(fill="x", padx=12)
        tabs = ttk.Notebook(self)
        tabs.pack(fill="both", expand=True, padx=12, pady=12)
        calls = ttk.Frame(tabs, padding=10)
        water = ttk.Frame(tabs, padding=10)
        tabs.add(calls, text="Учебные вызовы")
        tabs.add(water, text="Водоисточники")
        self.calls_tab(calls)
        self.water_tab(water)

    def calls_tab(self, parent):
        fields = ttk.Frame(parent)
        fields.pack(fill="x")
        ttk.Label(fields, text="Адрес (учебный)").grid(row=0, column=0, sticky="w")
        address = ttk.Entry(fields, width=44)
        address.grid(row=1, column=0, padx=(0, 10), sticky="ew")
        ttk.Label(fields, text="Описание").grid(row=0, column=1, sticky="w")
        description = ttk.Entry(fields, width=55)
        description.grid(row=1, column=1, padx=(0, 10), sticky="ew")
        fields.columnconfigure(1, weight=1)
        columns = ("id", "created", "address", "description", "status")
        tree = ttk.Treeview(parent, columns=columns, show="headings", selectmode="browse")
        for col, title, width in zip(columns,
                ("№", "Время", "Адрес", "Описание", "Статус"),
                (55, 155, 260, 320, 130)):
            tree.heading(col, text=title)
            tree.column(col, width=width, minwidth=55)
        tree.pack(fill="both", expand=True, pady=12)
        def refresh():
            for item in tree.get_children():
                tree.delete(item)
            for row in self.conn.execute(
                "SELECT id, created_at, address, description, status FROM incidents ORDER BY id DESC"
            ):
                tree.insert("", "end", values=row)
        def add():
            a, d = address.get().strip(), description.get().strip()
            if not a or not d:
                messagebox.showwarning("Поля", "Укажите адрес и описание")
                return
            self.conn.execute(
                "INSERT INTO incidents(created_at,address,description,status) VALUES (?,?,?,?)",
                (datetime.now().strftime("%Y-%m-%d %H:%M"), a, d, "Новый"),
            )
            self.conn.commit()
            address.delete(0, "end")
            description.delete(0, "end")
            refresh()
        def change_status():
            selected = tree.selection()
            if not selected:
                return
            incident_id = tree.item(selected[0], "values")[0]
            self.conn.execute("UPDATE incidents SET status=? WHERE id=?", ("Закрыт", incident_id))
            self.conn.commit()
            refresh()
        def export():
            path = filedialog.asksaveasfilename(defaultextension=".csv",
                filetypes=[("CSV", "*.csv")], initialfile="jarvis_fire_training_calls.csv")
            if not path:
                return
            with open(path, "w", newline="", encoding="utf-8-sig") as file:
                writer = csv.writer(file, delimiter=";")
                writer.writerow(["№", "Создан", "Адрес", "Описание", "Статус"])
                writer.writerows(self.conn.execute(
                    "SELECT id,created_at,address,description,status FROM incidents ORDER BY id"))
            messagebox.showinfo("Экспорт", "CSV сохранён")
        buttons = ttk.Frame(parent)
        buttons.pack(fill="x")
        ttk.Button(buttons, text="Создать учебный вызов", command=add).pack(side="left", padx=4)
        ttk.Button(buttons, text="Закрыть выбранный", command=change_status).pack(side="left", padx=4)
        ttk.Button(buttons, text="Экспорт CSV", command=export).pack(side="left", padx=4)
        refresh()

    def water_tab(self, parent):
        ttk.Label(parent, text="Точки вымышлены. Данные хранятся только на этом ПК.").pack(anchor="w")
        columns = ("name", "lat", "lon", "notes")
        tree = ttk.Treeview(parent, columns=columns, show="headings")
        for key, label in zip(columns, ("Название", "Широта", "Долгота", "Примечание")):
            tree.heading(key, text=label)
        tree.pack(fill="both", expand=True, pady=10)
        def refresh():
            for item in tree.get_children():
                tree.delete(item)
            for row in self.conn.execute(
                "SELECT name,latitude,longitude,notes FROM water_sources ORDER BY id"):
                tree.insert("", "end", values=row)
        refresh()
        form = ttk.Frame(parent)
        form.pack(fill="x")
        entries = []
        for idx, label in enumerate(("Название", "Широта", "Долгота", "Примечание")):
            ttk.Label(form, text=label).grid(row=0, column=idx, sticky="w")
            field = ttk.Entry(form, width=25)
            field.grid(row=1, column=idx, padx=5, sticky="ew")
            form.columnconfigure(idx, weight=1)
            entries.append(field)
        def add_source():
            try:
                name, lat, lon, notes = [field.get().strip() for field in entries]
                lat, lon = float(lat), float(lon)
                if not name or not (-90 <= lat <= 90 and -180 <= lon <= 180):
                    raise ValueError()
            except ValueError:
                messagebox.showwarning("Данные", "Укажите название и корректные координаты")
                return
            self.conn.execute(
                "INSERT INTO water_sources(name,latitude,longitude,notes) VALUES (?,?,?,?)",
                (name, lat, lon, notes))
            self.conn.commit()
            for field in entries:
                field.delete(0, "end")
            refresh()
        ttk.Button(parent, text="Добавить учебный водоисточник", command=add_source).pack(
            anchor="w", pady=10)

if __name__ == "__main__":
    App().mainloop()
