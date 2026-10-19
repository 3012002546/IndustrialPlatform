package com.industrialplatform.pda;

import java.io.File;

import com.getcapacitor.BridgeActivity;

public class MainActivity extends BridgeActivity {
    @Override
    public void onCreate(android.os.Bundle savedInstanceState) {
        registerPlugin(IndustrialRuntimePlugin.class);
        super.onCreate(savedInstanceState);
    }

    @Override
    protected void load() {
        super.load();
        File activeSlot = IndustrialRuntimePlugin.activeWebSlot(this);
        if (activeSlot != null && getBridge() != null) getBridge().setServerBasePath(activeSlot.getAbsolutePath());
    }
}
