"""Test logind on the private D-Bus bus created for the AppImage probe."""

import dbus
import dbus.service
from dbus.mainloop.glib import DBusGMainLoop
from gi.repository import GLib


SERVICE = "org.freedesktop.login1"
SESSION = SERVICE + ".Session"
MANAGER = SERVICE + ".Manager"
PROPERTIES = "org.freedesktop.DBus.Properties"
PROBE = "org.sqlitepass.Verification.Session"
SESSION_PATH = "/org/freedesktop/login1/session/sqlitepass_verification"


class Session(dbus.service.Object):
    def __init__(self, name):
        super().__init__(name, SESSION_PATH)
        self.locked = False
        self.observed = False

    @dbus.service.method(PROPERTIES, in_signature="ss", out_signature="v")
    def Get(self, interface, name):
        if interface != SESSION or name != "LockedHint":
            raise dbus.exceptions.DBusException("Unexpected session property")
        self.observed = True
        return dbus.Boolean(self.locked)

    @dbus.service.signal(PROPERTIES, signature="sa{sv}as")
    def PropertiesChanged(self, interface, values, invalidated):
        pass

    def set_locked(self, value):
        self.locked = value
        self.PropertiesChanged(SESSION, {"LockedHint": dbus.Boolean(value)}, [])


class Manager(dbus.service.Object):
    def __init__(self, name, session, loop):
        super().__init__(name, "/org/freedesktop/login1")
        self.session = session
        self.loop = loop

    @dbus.service.method(MANAGER, in_signature="s", out_signature="o")
    def GetSession(self, identifier):
        if identifier != "sqlitepass-verification":
            raise dbus.exceptions.DBusException("Unexpected test session")
        return SESSION_PATH

    @dbus.service.signal(MANAGER, signature="b")
    def PrepareForSleep(self, sleeping):
        pass

    @dbus.service.method(PROBE, in_signature="", out_signature="b")
    def WasObserved(self):
        return self.session.observed

    @dbus.service.method(PROBE, in_signature="b", out_signature="")
    def SetLocked(self, locked):
        self.session.set_locked(locked)

    @dbus.service.method(PROBE, in_signature="b", out_signature="")
    def SetSleeping(self, sleeping):
        self.PrepareForSleep(sleeping)

    @dbus.service.method(PROBE, in_signature="", out_signature="")
    def Stop(self):
        GLib.idle_add(self.loop.quit)


if __name__ == "__main__":
    DBusGMainLoop(set_as_default=True)
    loop = GLib.MainLoop()
    name = dbus.service.BusName(SERVICE, bus=dbus.SessionBus(), do_not_queue=True)
    session = Session(name)
    manager = Manager(name, session, loop)
    loop.run()
