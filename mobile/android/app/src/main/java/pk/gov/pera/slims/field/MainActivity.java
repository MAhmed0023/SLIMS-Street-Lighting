package pk.gov.pera.slims.field;

import android.os.Bundle;

import com.getcapacitor.BridgeActivity;

public class MainActivity extends BridgeActivity {
    @Override
    public void onCreate(Bundle savedInstanceState) {
        registerPlugin(FieldGuardPlugin.class);
        super.onCreate(savedInstanceState);
    }
}
