package pk.gov.pera.slims.field;

import android.Manifest;
import android.content.ContentResolver;
import android.content.Context;
import android.location.Location;
import android.location.LocationListener;
import android.location.LocationManager;
import android.os.Build;
import android.os.Bundle;
import android.os.Looper;
import android.provider.Settings;

import com.getcapacitor.JSObject;
import com.getcapacitor.PermissionState;
import com.getcapacitor.Plugin;
import com.getcapacitor.PluginCall;
import com.getcapacitor.PluginMethod;
import com.getcapacitor.annotation.CapacitorPlugin;
import com.getcapacitor.annotation.Permission;
import com.getcapacitor.annotation.PermissionCallback;

/**
 * Field safeguards for SLIMS evidence (FR-FLD-008, FR-FLD-011):
 *  - position fixes straight from Android's GPS and network providers, each carrying Android's own
 *    mock-location flag, so a location-faking app is detected rather than trusted;
 *  - the device identity used to bind the phone to one user (FR-SEC-009);
 *  - whether automatic date and time is on, for the clock-drift check.
 */
@CapacitorPlugin(
    name = "FieldGuard",
    permissions = {
        @Permission(alias = "location", strings = { Manifest.permission.ACCESS_FINE_LOCATION, Manifest.permission.ACCESS_COARSE_LOCATION }),
        @Permission(alias = "camera", strings = { Manifest.permission.CAMERA })
    }
)
public class FieldGuardPlugin extends Plugin {
    private LocationManager manager;
    private LocationListener listener;

    @PluginMethod
    public void info(PluginCall call) {
        ContentResolver cr = getContext().getContentResolver();
        JSObject o = new JSObject();
        o.put("uid", Settings.Secure.getString(cr, Settings.Secure.ANDROID_ID));
        o.put("model", Build.MODEL);
        o.put("manufacturer", Build.MANUFACTURER);
        o.put("osVersion", Build.VERSION.RELEASE);
        o.put("autoTime", Settings.Global.getInt(cr, Settings.Global.AUTO_TIME, 0) == 1);
        call.resolve(o);
    }

    @PluginMethod
    public void startWatch(PluginCall call) {
        if (getPermissionState("location") != PermissionState.GRANTED) {
            requestPermissionForAlias("location", call, "locationPermission");
            return;
        }
        begin(call);
    }

    @PermissionCallback
    private void locationPermission(PluginCall call) {
        if (getPermissionState("location") == PermissionState.GRANTED) begin(call);
        else call.reject("Location permission was refused. Allow it for SLIMS Field in the phone settings.");
    }

    private void begin(PluginCall call) {
        manager = (LocationManager) getContext().getSystemService(Context.LOCATION_SERVICE);
        stop();
        listener = new LocationListener() {
            @Override public void onLocationChanged(Location location) { notifyListeners("fix", toJs(location)); }
            @Override public void onStatusChanged(String provider, int status, Bundle extras) { }
            @Override public void onProviderEnabled(String provider) { }
            @Override public void onProviderDisabled(String provider) { }
        };
        try {
            boolean gps = manager.isProviderEnabled(LocationManager.GPS_PROVIDER);
            if (gps) manager.requestLocationUpdates(LocationManager.GPS_PROVIDER, 1000, 0, listener, Looper.getMainLooper());
            if (manager.isProviderEnabled(LocationManager.NETWORK_PROVIDER)) manager.requestLocationUpdates(LocationManager.NETWORK_PROVIDER, 2000, 0, listener, Looper.getMainLooper());
            Location last = manager.getLastKnownLocation(LocationManager.GPS_PROVIDER);
            if (last != null) notifyListeners("fix", toJs(last));
            JSObject r = new JSObject();
            r.put("gps", gps);
            call.resolve(r);
        } catch (SecurityException e) {
            call.reject("Location permission is missing.");
        }
    }

    @PluginMethod
    public void stopWatch(PluginCall call) {
        stop();
        call.resolve();
    }

    private void stop() {
        if (manager != null && listener != null) manager.removeUpdates(listener);
        listener = null;
    }

    private JSObject toJs(Location l) {
        JSObject o = new JSObject();
        o.put("lat", l.getLatitude());
        o.put("lng", l.getLongitude());
        o.put("accuracy", l.hasAccuracy() ? l.getAccuracy() : 9999);
        o.put("time", l.getTime());
        o.put("provider", l.getProvider());
        boolean mock = Build.VERSION.SDK_INT >= 31 ? l.isMock() : l.isFromMockProvider();
        o.put("isMock", mock);
        return o;
    }

    @Override
    protected void handleOnDestroy() {
        stop();
    }
}
