"""StatusNotifier watcher for the isolated Linux desktop verification bus."""

import dbus
import dbus.service
from dbus.mainloop.glib import DBusGMainLoop
from gi.repository import GLib


WATCHER = "org.kde.StatusNotifierWatcher"
PROPERTIES = "org.freedesktop.DBus.Properties"


class Watcher(dbus.service.Object):
    def __init__(self, name):
        super().__init__(name, "/StatusNotifierWatcher")
        self.items = []

    @dbus.service.method(WATCHER, in_signature="s", out_signature="")
    def RegisterStatusNotifierItem(self, service):
        if service not in self.items:
            self.items.append(service)

    @dbus.service.method(PROPERTIES, in_signature="s", out_signature="a{sv}")
    def GetAll(self, interface):
        if interface != WATCHER:
            return {}
        return {
            "IsStatusNotifierHostRegistered": dbus.Boolean(True),
            "RegisteredStatusNotifierItems": dbus.Array(self.items, signature="s"),
            "ProtocolVersion": dbus.Int32(0),
        }

    @dbus.service.method(PROPERTIES, in_signature="ss", out_signature="v")
    def Get(self, interface, property_name):
        return self.GetAll(interface)[property_name]


if __name__ == "__main__":
    DBusGMainLoop(set_as_default=True)
    bus_name = dbus.service.BusName(WATCHER, bus=dbus.SessionBus(), do_not_queue=True)
    watcher = Watcher(bus_name)
    GLib.MainLoop().run()
