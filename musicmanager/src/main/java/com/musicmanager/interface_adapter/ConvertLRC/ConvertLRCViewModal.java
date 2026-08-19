package com.musicmanager.interface_adapter.ConvertLRC;

import com.musicmanager.interface_adapter.ViewModel;

public class ConvertLRCViewModal extends ViewModel<ConvertLRCState> {

    public static final String VIEW_NAME = "convert lrc";

    public ConvertLRCViewModal() {
        super(VIEW_NAME);
        setState(new ConvertLRCState());
    }
}
